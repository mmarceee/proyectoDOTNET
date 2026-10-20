using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

public class QuitarEnvioDeRutaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static int _dia;
    private static DateOnly Fecha() => new DateOnly(2037, 1, 1).AddDays(Interlocked.Increment(ref _dia));
    private HttpClient Cliente() => factory.WithWebHostBuilder(b =>
        b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")).CreateClient();

    [Fact]
    public async Task Quitar_renumera_conserva_compromiso_e_historial_y_registra_T4_y_outbox()
    {
        using var client = Cliente();
        var a = await RecibidoAsync(client); var b = await RecibidoAsync(client); var c = await RecibidoAsync(client);
        var fecha = Fecha();
        var zona = Guid.Parse("61111111-1111-1111-1111-111111111111");
        var franja = Guid.Parse("71111111-1111-1111-1111-111111111111");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            var envio = (await db.Envios.SingleAsync(e => e.Id == b.Id));
            envio.DefinirPlanificacion(zona, franja, fecha);
            await db.SaveChangesAsync();
        }
        var ruta = await CrearAsync(client, fecha, a.Id, b.Id, c.Id);
        var historial = await client.GetStringAsync($"/api/planificacion/rutas/{ruta.Id}/validaciones");
        var actual = await LeerRutaAsync(await QuitarAsync(client, ruta, b.Id));
        Assert.Equal(ruta.Revision + 1, actual.Revision);
        Assert.Equal(new[] { a.Id, c.Id }, actual.Paradas.SelectMany(p => p.Envios).Select(e => e.EnvioId));
        Assert.Equal(new[] { 1, 2 }, actual.Paradas.Select(p => p.Orden));
        Assert.True(actual.ReservaActiva);
        Assert.Equal(historial, await client.GetStringAsync($"/api/planificacion/rutas/{ruta.Id}/validaciones"));
        var retiros = (await client.GetFromJsonAsync<List<RetiroEnvioRutaResponse>>($"/api/planificacion/rutas/{ruta.Id}/retiros"))!;
        var retiro = Assert.Single(retiros);
        Assert.Equal(b.Id, retiro.EnvioId); Assert.Equal(b.Numero, retiro.NumeroEnvio);
        Assert.Equal(2, retiro.OrdenAnterior); Assert.Equal(2, retiro.ParadasRestantes);
        Assert.Equal(actual.Revision, retiro.RevisionRuta);
        var pantalla = WebUtility.HtmlDecode(await client.GetStringAsync($"/backoffice/rutas/{ruta.Id}"));
        Assert.Contains($"Envío {b.Numero} quitado", pantalla);
        Assert.Contains(retiro.OcurridoEn.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture), pantalla);
        Assert.DoesNotContain("UTC−3", pantalla);

        using var leer = factory.Services.CreateScope();
        var envios = leer.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var quitado = await envios.Envios.Include(e => e.Eventos).SingleAsync(e => e.Id == b.Id);
        Assert.Equal(EstadoEnvio.EnDeposito, quitado.Estado);
        Assert.Equal(zona, quitado.ZonaId); Assert.Equal(franja, quitado.FranjaHorariaId);
        Assert.Equal(fecha, quitado.FechaEntregaProgramada);
        Assert.Single(quitado.Eventos, e => e.EstadoAnterior == EstadoEnvio.AsignadoARuta && e.EstadoNuevo == EstadoEnvio.EnDeposito);
        var mensaje = Assert.Single(await envios.Outbox.Where(m => m.Tipo == "EnvioDesasignadoDeRuta.v1").ToListAsync(),
            m => ContieneEnvio(m.Contenido, b.Id));
        using var datos = JsonDocument.Parse(mensaje.Contenido);
        Assert.NotEqual(Guid.Empty, mensaje.Id);
        Assert.Equal(ruta.Id, datos.RootElement.GetProperty("RutaId").GetGuid());
        var tenant = leer.ServiceProvider.GetRequiredService<ICurrentTenant>();
        Assert.Equal(tenant.OperadorId, mensaje.OperadorId);
        Assert.Equal(tenant.ComercioId, mensaje.ComercioId);
        Assert.False(datos.RootElement.TryGetProperty("MessageId", out _));
        Assert.False(datos.RootElement.TryGetProperty("OperadorId", out _));
        Assert.False(datos.RootElement.TryGetProperty("ComercioId", out _));
        Assert.False(datos.RootElement.TryGetProperty("OcurridoEn", out _));
        Assert.Equal("Backoffice", datos.RootElement.GetProperty("Origen").GetString());
        var disponibles = (await client.GetFromJsonAsync<EnviosDisponiblesResponse>(
            $"/api/planificacion/envios-disponibles?fecha={fecha:yyyy-MM-dd}&texto={b.Numero}"))!;
        Assert.Equal(b.Id, Assert.Single(disponibles.Items).Id);
    }

    [Fact]
    public async Task Quitar_el_ultimo_libera_recursos_y_el_envio_puede_reasignarse()
    {
        using var client = Cliente();
        var a = await RecibidoAsync(client); var b = await RecibidoAsync(client);
        var ruta = await CrearAsync(client, Fecha(), a.Id);
        var vacia = await LeerRutaAsync(await QuitarAsync(client, ruta, a.Id));
        Assert.Empty(vacia.Paradas); Assert.False(vacia.ReservaActiva); Assert.Equal("Planificada", vacia.Estado);
        var otra = await CrearAsync(client, ruta.Fecha, a.Id, b.Id);
        Assert.Contains(otra.Paradas.SelectMany(p => p.Envios), e => e.EnvioId == a.Id);
        var agregado = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios",
            new AgregarEnviosRutaRequest(vacia.Revision, [a.Id]));
        Assert.Equal(HttpStatusCode.Conflict, agregado.StatusCode);
    }

    [Theory]
    [InlineData("Despachada")]
    [InlineData("EnCurso")]
    [InlineData("Finalizada")]
    public async Task Una_ruta_no_planificada_no_ofrece_ni_permite_quitar(string estado)
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client);
        var ruta = await CrearAsync(client, Fecha(), envio.Id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            var entidad = (await db.Rutas.Include(r => r.Paradas).SingleAsync(r => r.Id == ruta.Id));
            entidad.Despachar(DateTimeOffset.UtcNow);
            if (estado == "EnCurso") entidad.Iniciar();
            if (estado == "Finalizada") entidad.Finalizar();
            await db.SaveChangesAsync();
        }
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await QuitarAsync(client, ruta, envio.Id)).StatusCode);
            var html = await client.GetStringAsync($"/backoffice/rutas/{ruta.Id}");
            Assert.DoesNotContain("data-editar-paradas", html);
            Assert.DoesNotContain("Confirmar quitar envío", WebUtility.HtmlDecode(html));
            await ExigirAsignadoAsync(envio.Id);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            var entidad = await db.Rutas.Include(r => r.Paradas).SingleAsync(r => r.Id == ruta.Id);
            if (entidad.Estado != EstadoRuta.Finalizada) { entidad.Finalizar(); await db.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task Envio_ajeno_a_la_ruta_y_revision_obsoleta_no_cambian_datos()
    {
        using var client = Cliente();
        var a = await RecibidoAsync(client); var b = await RecibidoAsync(client);
        var ruta = await CrearAsync(client, Fecha(), a.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await QuitarAsync(client, ruta, b.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await QuitarAsync(client, ruta with { Revision = ruta.Revision + 1 }, a.Id)).StatusCode);
        await ExigirAsignadoAsync(a.Id);
        using var leer = factory.Services.CreateScope();
        Assert.Equal(EstadoEnvio.EnDeposito, (await leer.ServiceProvider.GetRequiredService<EnviosDbContext>().Envios.SingleAsync(e => e.Id == b.Id)).Estado);
    }

    [Fact]
    public async Task Dos_confirmaciones_no_producen_dos_transiciones_ni_dos_mensajes()
    {
        using var a = Cliente(); using var b = Cliente();
        var envio = await RecibidoAsync(a);
        var ruta = await CrearAsync(a, Fecha(), envio.Id);
        var resultados = await Task.WhenAll(QuitarAsync(a, ruta, envio.Id), QuitarAsync(b, ruta, envio.Id));
        Assert.Single(resultados, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(resultados, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var actual = await db.Envios.Include(e => e.Eventos).SingleAsync(e => e.Id == envio.Id);
        Assert.Single(actual.Eventos, e => e.EstadoAnterior == EstadoEnvio.AsignadoARuta && e.EstadoNuevo == EstadoEnvio.EnDeposito);
        Assert.Equal(1, (await db.Outbox.Where(m => m.Tipo == "EnvioDesasignadoDeRuta.v1").ToListAsync())
            .Count(m => ContieneEnvio(m.Contenido, envio.Id)));
    }

    [Fact]
    public async Task Fallo_despues_de_guardar_Envios_revierte_parada_estado_evento_y_outbox()
    {
        using var normal = Cliente();
        var envio = await RecibidoAsync(normal);
        var ruta = await CrearAsync(normal, Fecha(), envio.Id);
        using var host = factory.WithWebHostBuilder(b => b
            .UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")
            .ConfigureTestServices(s => s.AddScoped<IRutaRepository>(p =>
                new RepositorioQueFalla(new RutaRepository(p.GetRequiredService<PlanificacionDbContext>())))));
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.InternalServerError, (await QuitarAsync(client, ruta, envio.Id)).StatusCode);
        await ExigirAsignadoAsync(envio.Id);
        var actual = (await normal.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!;
        Assert.Equal(ruta.Revision, actual.Revision); Assert.Single(actual.Paradas);
        Assert.Empty((await normal.GetFromJsonAsync<List<RetiroEnvioRutaResponse>>($"/api/planificacion/rutas/{ruta.Id}/retiros"))!);
    }

    [Fact]
    public async Task Otra_empresa_no_quita_paradas_de_la_ruta()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client);
        var ruta = await CrearAsync(client, Fecha(), envio.Id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            db.Operadores.Add(Operador.Crear("Otra empresa CU41", "otra-cu41", "America/Montevideo",
                Guid.Parse("13333333-3333-3333-3333-333333333333")));
            await db.SaveChangesAsync();
        }
        using var host = factory.WithWebHostBuilder(b => b
            .UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")
            .UseSetting("Database:MigrateOnStartup", "false")
            .UseSetting("TenantProvisorio:OperadorId", "13333333-3333-3333-3333-333333333333"));
        using var ajeno = host.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await QuitarAsync(ajeno, ruta, envio.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ajeno.GetAsync($"/api/planificacion/rutas/{ruta.Id}/retiros")).StatusCode);
        await ExigirAsignadoAsync(envio.Id);
    }

    [Fact]
    public async Task Pantalla_confirma_con_antiforgery_y_muestra_la_ruta_vacia()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client);
        var ruta = await CrearAsync(client, Fecha(), envio.Id);
        var detalle = $"/backoffice/rutas/{ruta.Id}";
        var html = await client.GetStringAsync(detalle);
        Assert.Contains("data-editar-paradas", html);
        Assert.Contains("data-envio-id=\"" + envio.Id + "\"", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        Assert.Equal(HttpStatusCode.BadRequest, (await QuitarAsync(client, ruta, envio.Id)).StatusCode);
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["EnvioAQuitar"] = envio.Id.ToString(), ["RevisionEsperada"] = ruta.Revision.ToString(),
            ["Fecha"] = ruta.Fecha.ToString("yyyy-MM-dd"),
        };
        var response = await client.PostAsync(detalle + "?handler=QuitarEnvio", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("la ruta quedó vacía", resultado);
        Assert.Contains("La ruta no tiene envíos", resultado);
        Assert.Contains($"Envío {envio.Numero} quitado", resultado);
    }

    [Fact]
    public async Task Quitar_y_despachar_con_igual_revision_no_se_confirman_ambos()
    {
        using var client = Cliente();
        var a = await RecibidoAsync(client); var b = await RecibidoAsync(client);
        var ruta = await CrearAsync(client, Fecha(), a.Id, b.Id);
        var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Despachar()
        {
            await inicio.Task;
            using var scope = factory.Services.CreateScope();
            var unidad = scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>();
            try
            {
                await unidad.EjecutarAsync(async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IAdministracionModuleApi>().BloquearPlanificacionAsync(default);
                    var repositorio = scope.ServiceProvider.GetRequiredService<IRutaRepository>();
                    var actual = (await repositorio.ObtenerAsync(ruta.Id, default))!;
                    if (actual.Revision != ruta.Revision) throw new ConflictoRutaException("RutaModificada", "Revisión obsoleta.");
                    actual.Despachar(DateTimeOffset.UtcNow);
                    await repositorio.GuardarAsync(default);
                }, default);
                return true;
            }
            catch (ConflictoRutaException) { return false; }
        }
        async Task<HttpResponseMessage> Quitar()
        {
            await inicio.Task;
            return await QuitarAsync(client, ruta, a.Id);
        }
        var despacho = Despachar(); var baja = Quitar(); inicio.SetResult();
        await Task.WhenAll(despacho, baja);
        try
        {
            var despachada = await despacho;
            Assert.Equal(despachada ? HttpStatusCode.Conflict : HttpStatusCode.OK, (await baja).StatusCode);
            var actual = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!;
            Assert.Equal(ruta.Revision + 1, actual.Revision);
            Assert.Equal(despachada ? 2 : 1, actual.Paradas.Count);
            if (despachada) await ExigirAsignadoAsync(a.Id);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            var actual = await db.Rutas.Include(r => r.Paradas).SingleAsync(r => r.Id == ruta.Id);
            if (actual.Estado == EstadoRuta.Despachada) { actual.Finalizar(); await db.SaveChangesAsync(); }
        }
    }

    private static async Task<HttpResponseMessage> QuitarAsync(HttpClient client, RutaResponse ruta, Guid envioId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/planificacion/rutas/{ruta.Id}/envios/{envioId}")
            { Content = JsonContent.Create(new QuitarEnvioDeRutaRequest(ruta.Revision)) };
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Confirmacion_obsoleta_en_la_pantalla_muestra_conflicto_y_recarga_la_revision()
    {
        using var client = Cliente(); using var otro = Cliente();
        var a = await RecibidoAsync(client); var b = await RecibidoAsync(client);
        var ruta = await CrearAsync(client, Fecha(), a.Id, b.Id);
        var url = $"/backoffice/rutas/{ruta.Id}";
        var html = await client.GetStringAsync(url);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var actual = await LeerRutaAsync(await QuitarAsync(otro, ruta, b.Id));
        var respuesta = await client.PostAsync(url + "?handler=QuitarEnvio", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
                ["EnvioAQuitar"] = a.Id.ToString(), ["RevisionEsperada"] = ruta.Revision.ToString(),
                ["Fecha"] = ruta.Fecha.ToString("yyyy-MM-dd"),
            }));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var resultado = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains("Otra operación modificó la ruta", resultado);
        var revision = Regex.Match(resultado, "name=\"RevisionEsperada\"[^>]*value=\"([^\"]+)\"");
        Assert.Equal(actual.Revision.ToString(), revision.Groups[1].Value);
        await ExigirAsignadoAsync(a.Id);
    }
    private static async Task<RutaResponse> CrearAsync(HttpClient client, DateOnly fecha, params Guid[] envios)
        => await LeerRutaAsync(await client.PostAsJsonAsync("/api/planificacion/rutas", new CrearRutaRequest(fecha, Repartidor, Vehiculo, envios)));
    private static async Task<RutaResponse> LeerRutaAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RutaResponse>())!;
    }
    private static async Task<CrearEnvioResponse> RecibidoAsync(HttpClient client)
    {
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana CU41", "099123456", null, null),
            new DireccionRequest("Av. Italia", Guid.NewGuid().ToString("N"), "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        creado.EnsureSuccessStatusCode();
        var envio = (await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        (await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest(envio.Numero + "-1", null))).EnsureSuccessStatusCode();
        return envio;
    }
    private async Task ExigirAsignadoAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var envio = await db.Envios.Include(e => e.Eventos).SingleAsync(e => e.Id == id);
        Assert.Equal(EstadoEnvio.AsignadoARuta, envio.Estado);
        Assert.DoesNotContain(envio.Eventos, e => e.EstadoAnterior == EstadoEnvio.AsignadoARuta && e.EstadoNuevo == EstadoEnvio.EnDeposito);
        Assert.DoesNotContain(await db.Outbox.Where(m => m.Tipo == "EnvioDesasignadoDeRuta.v1").ToListAsync(),
            m => ContieneEnvio(m.Contenido, id));
    }
    private static bool ContieneEnvio(string contenido, Guid id)
    {
        using var datos = JsonDocument.Parse(contenido);
        return datos.RootElement.GetProperty("EnvioId").GetGuid() == id;
    }
    private sealed class RepositorioQueFalla(IRutaRepository original) : IRutaRepository
    {
        public Task<IReadOnlyList<Ruta>> ListarDelRepartidorAsync(Guid id, int pagina, CancellationToken ct) => original.ListarDelRepartidorAsync(id, pagina, ct);
        public Task<Ruta?> ObtenerAsync(Guid id, CancellationToken ct) => original.ObtenerAsync(id, ct);
        public Task<IReadOnlyList<Ruta>> ListarAsync(DateOnly? fecha, EstadoRuta? estado, CancellationToken ct) => original.ListarAsync(fecha, estado, ct);
        public Task<IReadOnlyList<Guid>> EnviosOcupadosAsync(IReadOnlyCollection<Guid> ids, Guid? propia, CancellationToken ct) => original.EnviosOcupadosAsync(ids, propia, ct);
        public Task<bool> HayActivasConEnviosAsync(IReadOnlyCollection<Guid> ids, DateOnly desde, CancellationToken ct) => original.HayActivasConEnviosAsync(ids, desde, ct);
        public Task<IReadOnlyList<string>> RecursosOcupadosAsync(DateOnly fecha, Guid repartidor, Guid vehiculo, Guid? propia, CancellationToken ct) => original.RecursosOcupadosAsync(fecha, repartidor, vehiculo, propia, ct);
        public Task<IReadOnlyList<ValidacionRuta>> ValidacionesAsync(Guid id, int pagina, CancellationToken ct) => original.ValidacionesAsync(id, pagina, ct);
        public void Agregar(Ruta ruta) => original.Agregar(ruta);
        public void Agregar(ValidacionRuta validacion) => original.Agregar(validacion);
        public Task<IReadOnlyList<RetiroEnvioRuta>> RetirosAsync(Guid id, int pagina, CancellationToken ct) => original.RetirosAsync(id, pagina, ct);
        public void Agregar(RetiroEnvioRuta retiro) => original.Agregar(retiro);
        public Task<IReadOnlyList<AnulacionDespacho>> AnulacionesAsync(Guid id, int pagina, CancellationToken ct) => original.AnulacionesAsync(id, pagina, ct);
        public void Agregar(AnulacionDespacho anulacion) => original.Agregar(anulacion);
        public Task GuardarAsync(CancellationToken ct) => throw new InvalidOperationException("Fallo después de guardar T4.");
    }
}
