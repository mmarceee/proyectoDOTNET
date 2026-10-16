using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Envios;

// CU-19 de punta a punta, con sesiones reales: el despachador elige la fecha y la franja del nuevo intento de un envío
// que falló el 22/10 a las 11:30 de Montevideo con 24 h de plazo (se reintenta desde el 23/10, 14:30 UTC). Usa la
// zona y las franjas del operador Demo de los datos iniciales: mañana de 9 a 13 y tarde de 14 a 18, todos los días.
public class ReprogramarEnvioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid ZonaDemo = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly Guid FranjaManana = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly Guid FranjaTarde = Guid.Parse("72222222-2222-2222-2222-222222222222");
    private static readonly DateOnly DiaSiguiente = new(2026, 10, 23);

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    [Fact]
    public async Task El_despachador_elige_una_franja_despues_del_plazo_y_queda_con_su_aviso()
    {
        var envio = await ReprogramadoAsync();
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        // La franja de la tarde empieza a las 14:00 de Montevideo (17:00 UTC): después del plazo.
        var respuesta = await despacho.PostAsJsonAsync($"/api/envios/{envio.Id}/reprogramacion",
            new ReprogramarEnvioRequest(DiaSiguiente, FranjaTarde));

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(FranjaTarde, await ConsultarAsync<Guid>("""SELECT "FranjaHorariaId" FROM envios."Envios" WHERE "Id" = @id""", envio.Id));
        Assert.Equal("Reprogramado", await ConsultarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id" = @id""", envio.Id));
        Assert.Equal(1L, await ConsultarAsync<long>(
            """SELECT count(*) FROM envios.outbox_messages WHERE "Tipo" = 'EnvioReprogramado.v1' AND "Contenido"::jsonb ->> 'EnvioId' = @id::text""",
            envio.Id));
    }

    // A1: la franja de la mañana empieza a las 9:00 de Montevideo (12:00 UTC), antes del plazo.
    [Fact]
    public async Task Una_franja_antes_del_plazo_entre_intentos_se_rechaza()
    {
        var envio = await ReprogramadoAsync();
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var respuesta = await despacho.PostAsJsonAsync($"/api/envios/{envio.Id}/reprogramacion",
            new ReprogramarEnvioRequest(DiaSiguiente, FranjaManana));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("plazo entre intentos", problema?.Detail);
    }

    // Sólo el despachador reprograma.
    [Fact]
    public async Task El_comercio_no_puede_reprogramar()
    {
        var envio = await ReprogramadoAsync();
        using var comercio = Cliente();
        await Sesion.IngresarAsync(comercio, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(comercio, DatosIniciales.OperadorDemoId);

        var respuesta = await comercio.PostAsJsonAsync($"/api/envios/{envio.Id}/reprogramacion",
            new ReprogramarEnvioRequest(DiaSiguiente, FranjaTarde));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    // Aislamiento: para el despachador de otro operador, el envío no existe.
    [Fact]
    public async Task El_despachador_de_otro_operador_recibe_404()
    {
        var envio = await ReprogramadoAsync();
        using var otro = Cliente();
        await Sesion.IngresarAsync(otro, "despacho@envios-del-este.uy");

        var respuesta = await otro.PostAsJsonAsync($"/api/envios/{envio.Id}/reprogramacion",
            new ReprogramarEnvioRequest(DiaSiguiente, FranjaTarde));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    // Un envío de Demo en la zona de Montevideo, llevado hasta Reprogramado por el camino real: T2, T3, T5 y el
    // intento fallido de CU-18 (T7 y T9). Se guarda con el operador Demo como inquilino.
    private async Task<Envio> ReprogramadoAsync()
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        var salida = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
        var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, $"ENV-R-{Guid.NewGuid():N}",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, salida.AddDays(-1));
        envio.DefinirPlanificacion(ZonaDemo, null, null);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, salida.AddHours(-12));
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, salida.AddHours(-1));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, salida);
        envio.RegistrarIntentoFallido(new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false),
            null, null, new Ubicacion(-34.9m, -56.16m), new ReglasEntrega(false, false, maxIntentos: 3, TimeSpan.FromHours(24)),
            null, new DateTimeOffset(2026, 10, 22, 11, 30, 0, TimeSpan.FromHours(-3)));

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    // Lee directo de la base, sin pasar por la API ni por el filtro, para comprobar lo que quedó guardado.
    private async Task<T> ConsultarAsync<T>(string sql, Guid envioId)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", envioId);
        return (T)(await comando.ExecuteScalarAsync())!;
    }
}
