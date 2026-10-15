using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

public sealed class PlazoEntreIntentosTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly Guid Zona = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly Guid Manana = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly Guid Tarde = Guid.Parse("72222222-2222-2222-2222-222222222222");

    private HttpClient Cliente() => factory.WithWebHostBuilder(b =>
        b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")).CreateClient();

    [Theory]
    [InlineData(0, 9, 11, 30, 0, false)]
    [InlineData(1, 14, 11, 30, 0, true)]
    [InlineData(2, 9, 9, 0, 0, true)]
    [InlineData(3, 9, 9, 0, 1, false)]
    [InlineData(4, 0, 11, 30, 0, false)]
    [InlineData(5, 0, 0, 0, 0, true)]
    public async Task Listado_armado_y_despacho_respetan_el_inicio_local_y_el_limite_exacto(
        int caso, int franja, int hora, int minuto, int segundo, bool permitido)
    {
        using var client = Cliente();
        var fecha = new DateOnly(2035, 10, 23).AddDays(caso);
        var limiteLocal = new DateTimeOffset(fecha.ToDateTime(new(hora, minuto, segundo)), TimeSpan.FromHours(-3));
        var envio = await ReprogramadoAsync(franja, limiteLocal);

        var listado = (await client.GetFromJsonAsync<EnviosDisponiblesResponse>(
            $"/api/planificacion/envios-disponibles?fecha={fecha:yyyy-MM-dd}&texto={envio.Numero}"))!;
        var disponible = Assert.Single(listado.Items);
        Assert.Equal(limiteLocal, disponible.ReintentoDesde);
        Assert.Equal(TimeSpan.FromHours(-3), disponible.ReintentoDesde!.Value.Offset);
        Assert.Equal(permitido, disponible.DisponibleParaFecha);

        var preview = await client.PostAsJsonAsync("/api/planificacion/rutas/prevalidacion",
            new PrevalidarRutaRequest(fecha, Repartidor, Vehiculo, [envio.Id]));
        preview.EnsureSuccessStatusCode();
        var evaluacion = (await preview.Content.ReadFromJsonAsync<EvaluacionRutaResponse>())!;
        Assert.Equal(permitido, evaluacion.Valida);
        Assert.Equal(!permitido, evaluacion.Restricciones.Any(r => r.Codigo == "PlazoEntreIntentos" && r.EnvioId == envio.Id));

        var creada = await client.PostAsJsonAsync("/api/planificacion/rutas",
            new CrearRutaRequest(fecha, Repartidor, Vehiculo, [envio.Id]));
        Assert.True(creada.StatusCode == (permitido ? HttpStatusCode.Created : HttpStatusCode.BadRequest),
            await creada.Content.ReadAsStringAsync());
        if (!permitido)
        {
            Assert.Contains("PlazoEntreIntentos", await creada.Content.ReadAsStringAsync());
            using var scope = factory.Services.CreateScope();
            Assert.Equal(EstadoEnvio.Reprogramado, (await scope.ServiceProvider.GetRequiredService<EnviosDbContext>()
                .Envios.SingleAsync(e => e.Id == envio.Id)).Estado);
            Assert.False(await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>()
                .Rutas.AnyAsync(r => r.Fecha == fecha));
            return;
        }

        var ruta = (await creada.Content.ReadFromJsonAsync<RutaResponse>())!;
        var despacho = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho",
            new DespacharRutaRequest(ruta.Revision));
        despacho.EnsureSuccessStatusCode();
        using var finalizacion = factory.Services.CreateScope();
        var rutas = finalizacion.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        (await rutas.Rutas.SingleAsync(r => r.Id == ruta.Id)).Finalizar();
        await rutas.SaveChangesAsync();
    }

    [Fact]
    public async Task Despacho_relee_el_plazo_aunque_el_envio_ya_este_asignado()
    {
        using var client = Cliente();
        var fecha = new DateOnly(2035, 11, 10);
        var limite = new DateTimeOffset(2035, 11, 10, 0, 0, 0, TimeSpan.FromHours(-3));
        var envio = await ReprogramadoAsync(0, limite);
        var creada = await client.PostAsJsonAsync("/api/planificacion/rutas",
            new CrearRutaRequest(fecha, Repartidor, Vehiculo, [envio.Id]));
        creada.EnsureSuccessStatusCode();
        var ruta = (await creada.Content.ReadFromJsonAsync<RutaResponse>())!;

        // Simula que la restricción cambió después del armado: el despacho debe releer Envíos.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            var id = envio.Id;
            var nuevoLimite = limite.AddHours(1).ToUniversalTime();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE envios.\"Envios\" SET \"ReintentoDesde\"={nuevoLimite} WHERE \"Id\"={id}");
        }
        foreach (var accion in new[] { "despacho/prevalidacion", "despacho" })
        {
            var respuesta = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/{accion}",
                new DespacharRutaRequest(ruta.Revision));
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Contains("PlazoEntreIntentos", await respuesta.Content.ReadAsStringAsync());
        }
        var actual = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!;
        Assert.Equal("Planificada", actual.Estado);
        Assert.Equal(ruta.Revision, actual.Revision);
    }

    private async Task<Envio> ReprogramadoAsync(int franja, DateTimeOffset limiteLocal)
    {
        using var scope = factory.Services.CreateScope();
        var operador = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().OperadorId!.Value;
        var fallo = limiteLocal.AddDays(-1);
        var envio = Envio.Crear(operador, Guid.NewGuid(), $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 100)], OrigenEvento.PortalComercio, null, fallo.AddDays(-1));
        envio.DefinirPlanificacion(Zona, franja == 9 ? Manana : franja == 14 ? Tarde : null, null);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, fallo.AddHours(-5));
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, fallo.AddHours(-4));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, fallo.AddHours(-3));
        envio.RegistrarIntentoFallido(new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", true, false), null, null,
            new Ubicacion(-34.9m, -56.16m), new ReglasEntrega(false, false, 3, TimeSpan.FromHours(24)), null, fallo);
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }
}
