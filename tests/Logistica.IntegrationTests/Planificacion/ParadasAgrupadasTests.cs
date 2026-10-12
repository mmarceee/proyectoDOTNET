using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

public sealed class ParadasAgrupadasTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int _dia;
    private static DateOnly Fecha() => new DateOnly(2041, 1, 1).AddDays(10 * Interlocked.Increment(ref _dia));
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private HttpClient Cliente() => factory.WithWebHostBuilder(b => b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")).CreateClient();

    [Fact]
    public async Task Agrupa_apartamentos_y_quita_un_envio_sin_eliminar_la_visita()
    {
        using var client = Cliente();
        var a = await Recibido(client, "Apartamento 101"); var b = await Recibido(client, "Apartamento 202"); var c = await Recibido(client, "Apartamento 303");
        var fecha = Fecha();
        var evaluar = await client.PostAsJsonAsync("/api/planificacion/rutas/prevalidacion", new PrevalidarRutaRequest(fecha, Repartidor, Vehiculo, [a.Id, b.Id, c.Id], null, null));
        evaluar.EnsureSuccessStatusCode();
        Assert.Equal(1, (await evaluar.Content.ReadFromJsonAsync<EvaluacionRutaResponse>())!.Paradas);
        var ruta = await Crear(client, fecha, a.Id, b.Id, c.Id);
        var parada = Assert.Single(ruta.Paradas); Assert.Equal(3, parada.Envios.Count);
        var respuesta = await Quitar(client, ruta, b.Id);
        var actual = (await respuesta.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal(parada.Id, Assert.Single(actual.Paradas).Id);
        Assert.Equal(new[] { a.Id, c.Id }.Order(), actual.Paradas[0].Envios.Select(e => e.EnvioId).Order());
        Assert.True(actual.ReservaActiva);
        var retiros = (await client.GetFromJsonAsync<List<RetiroEnvioRutaResponse>>($"/api/planificacion/rutas/{ruta.Id}/retiros"))!;
        Assert.Equal(1, Assert.Single(retiros).ParadasRestantes);
        // La exclusividad RF15 sigue siendo por envío.
        var duplicado = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(Fecha(), a.Id));
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
        var otra = await Crear(client, Fecha(), b.Id); Assert.Single(otra.Paradas);
        var html = await client.GetStringAsync($"/backoffice/rutas/{ruta.Id}");
        Assert.Contains("Parada 1", html); Assert.DoesNotContain("Parada 2", html);
    }

    [Fact]
    public async Task Agregar_otro_envio_a_la_direccion_no_crea_otra_parada_y_quitar_el_ultimo_la_elimina()
    {
        using var client = Cliente(); var a = await Recibido(client, "101"); var b = await Recibido(client, "202");
        var ruta = await Crear(client, Fecha(), a.Id);
        var agregado = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios", new AgregarEnviosRutaRequest(ruta.Revision, [b.Id]));
        agregado.EnsureSuccessStatusCode(); var actual = (await agregado.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal(ruta.Paradas[0].Id, Assert.Single(actual.Paradas).Id); Assert.Equal(2, actual.Paradas[0].Envios.Count);
        actual = (await (await Quitar(client, actual, a.Id)).Content.ReadFromJsonAsync<RutaResponse>())!;
        actual = (await (await Quitar(client, actual, b.Id)).Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Empty(actual.Paradas); Assert.False(actual.ReservaActiva);
    }

    [Fact]
    public async Task Misma_direccion_con_manana_y_tarde_tiene_dos_visitas_y_un_envio_libre_no_se_duplica()
    {
        using var client = Cliente(); var a = await Recibido(client, "101"); var b = await Recibido(client, "202"); var libre = await Recibido(client, "303");
        var fecha = Fecha();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            var zona = Guid.Parse("61111111-1111-1111-1111-111111111111");
            (await db.Envios.SingleAsync(e => e.Id == a.Id)).DefinirPlanificacion(zona, Guid.Parse("71111111-1111-1111-1111-111111111111"), fecha);
            (await db.Envios.SingleAsync(e => e.Id == b.Id)).DefinirPlanificacion(zona, Guid.Parse("72222222-2222-2222-2222-222222222222"), fecha);
            await db.SaveChangesAsync();
        }
        var ruta = await Crear(client, fecha, libre.Id, b.Id, a.Id);
        Assert.Equal(2, ruta.Paradas.Count);
        Assert.Single(ruta.Paradas.SelectMany(p => p.Envios), e => e.EnvioId == libre.Id);
        Assert.Contains(ruta.Paradas[0].Envios, e => e.EnvioId == libre.Id);
    }

    [Fact]
    public async Task Migracion_de_ida_y_vuelta_conserva_envios_y_evidencia_y_agrupa_las_rutas_anteriores()
    {
        using var client = Cliente(); var a = await Recibido(client, "101"); var b = await Recibido(client, "202"); var c = await Recibido(client, "303");
        var ruta = await Crear(client, Fecha(), a.Id, b.Id, c.Id);
        var evidencia = await client.GetStringAsync($"/api/planificacion/rutas/{ruta.Id}/validaciones");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20261012134731_HistorialRetirosCU41");
            await db.Database.MigrateAsync();
        }
        var actual = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!;
        Assert.Equal(3, Assert.Single(actual.Paradas).Envios.Count);
        Assert.Equal(evidencia, await client.GetStringAsync($"/api/planificacion/rutas/{ruta.Id}/validaciones"));
        Assert.Equal(ruta.Revision + 1, actual.Revision);
    }

    private static CrearRutaRequest Pedido(DateOnly fecha, params Guid[] ids) => new(fecha, Repartidor, Vehiculo, ids);
    [Fact]
    public async Task Cambiar_fecha_unifica_visitas_con_franjas_reemplazadas_y_conserva_todos_los_envios()
    {
        using var client = Cliente(); var a = await Recibido(client, "101"); var b = await Recibido(client, "202");
        var fecha = Fecha();
        var nuevaFecha = fecha.AddDays(1);
        using (var scope = factory.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            var operador = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var zona = Guid.Parse("61111111-1111-1111-1111-111111111111");
            var manana = FranjaHoraria.Crear(operador, zona, new(9, 0), new(13, 0), Enum.GetValues<DayOfWeek>(), fecha);
            var tarde = FranjaHoraria.Crear(operador, zona, new(14, 0), new(18, 0), Enum.GetValues<DayOfWeek>(), fecha);
            admin.Franjas.AddRange(manana, tarde); admin.Franjas.Add(tarde.Reemplazar(new(9, 0), new(13, 0), Enum.GetValues<DayOfWeek>(), nuevaFecha));
            await admin.SaveChangesAsync();
            var envios = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            (await envios.Envios.SingleAsync(e => e.Id == a.Id)).DefinirPlanificacion(zona, manana.Id, null);
            (await envios.Envios.SingleAsync(e => e.Id == b.Id)).DefinirPlanificacion(zona, tarde.Id, null);
            await envios.SaveChangesAsync();
        }
        var ruta = await Crear(client, fecha, a.Id, b.Id); Assert.Equal(2, ruta.Paradas.Count);
        var r = await client.PutAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/planificacion", new ModificarRutaRequest(ruta.Revision, nuevaFecha, Repartidor, Vehiculo));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var actual = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!;
        Assert.Equal(2, Assert.Single(actual.Paradas).Envios.Count);
        Assert.Equal(ruta.Revision + 1, actual.Revision);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20261012134731_HistorialRetirosCU41");
            await db.Database.MigrateAsync();
        }
        actual = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!;
        Assert.Equal(2, Assert.Single(actual.Paradas).Envios.Count);
    }
    private static async Task<RutaResponse> Crear(HttpClient client, DateOnly fecha, params Guid[] ids)
    { var r = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(fecha, ids)); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<RutaResponse>())!; }
    private static async Task<HttpResponseMessage> Quitar(HttpClient client, RutaResponse ruta, Guid envio)
    { using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/planificacion/rutas/{ruta.Id}/envios/{envio}") { Content = JsonContent.Create(new QuitarEnvioDeRutaRequest(ruta.Revision)) }; var r = await client.SendAsync(req); r.EnsureSuccessStatusCode(); return r; }
    private static async Task<CrearEnvioResponse> Recibido(HttpClient client, string referencia)
    {
        var r = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(new DestinatarioRequest("Ana", "099123456", null, null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", referencia), [new BultoRequest(2, 30, 20, 10)]));
        r.EnsureSuccessStatusCode(); var envio = (await r.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        (await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest(envio.Numero + "-1", null))).EnsureSuccessStatusCode();
        return envio;
    }
}
