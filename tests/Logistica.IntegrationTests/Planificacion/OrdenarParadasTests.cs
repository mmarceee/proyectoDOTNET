using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

public class OrdenarParadasTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int dia;
    private HttpClient Cliente() => factory.WithWebHostBuilder(b =>
        b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")).CreateClient(Sesion.Opciones);
    private string Url(RutaResponse ruta) => $"/api/planificacion/rutas/{ruta.Id}/orden";

    [Fact]
    public async Task Proponer_no_persiste_y_confirmar_conserva_identidad_envios_y_evidencia()
    {
        using var client = Cliente(); var ruta = await CrearAsync(client);
        var evidencia = await client.GetStringAsync($"/api/planificacion/rutas/{ruta.Id}/validaciones");
        var propuesto = await ProponerAsync(client, ruta);
        Assert.Equal(ruta.Paradas.Reverse().Select(p => p.Id), propuesto.Paradas.Select(p => p.Id));
        var sinGuardar = await LeerAsync(client, ruta.Id);
        Assert.Equal(ruta.Revision, sinGuardar.Revision);
        Assert.Equal(ruta.Paradas.Select(p => p.Id), sinGuardar.Paradas.Select(p => p.Id));
        var response = await client.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, propuesto.Paradas.Select(p => p.Id).ToArray()));
        response.EnsureSuccessStatusCode();
        var actual = (await response.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal(ruta.Revision + 1, actual.Revision);
        Assert.Equal(propuesto.Paradas.Select(p => p.Id), actual.Paradas.Select(p => p.Id));
        Assert.Equal(new[] { 1, 2 }, actual.Paradas.Select(p => p.Orden));
        Assert.Equal(ruta.Paradas.SelectMany(p => p.Envios).Select(e => e.EnvioId).Order(), actual.Paradas.SelectMany(p => p.Envios).Select(e => e.EnvioId).Order());
        Assert.Equal(2, actual.Paradas.Single(p => p.Id == ruta.Paradas[0].Id).Envios.Count);
        Assert.Equal(evidencia, await client.GetStringAsync($"/api/planificacion/rutas/{ruta.Id}/validaciones"));
        Assert.True(actual.ReservaActiva);
    }

    [Fact]
    public async Task Orden_manual_advierte_y_exige_confirmacion_sin_guardar_en_el_rechazo()
    {
        using var client = Cliente(); var ruta = await CrearAsync(client);
        var orden = ruta.Paradas.Select(p => p.Id).ToArray(); // Tarde antes de mañana.
        var revisar = await client.PostAsJsonAsync(Url(ruta) + "/prevalidacion", new RevisarOrdenParadasRequest(ruta.Revision, orden));
        revisar.EnsureSuccessStatusCode();
        Assert.Single((await revisar.Content.ReadFromJsonAsync<OrdenParadasResponse>())!.Advertencias);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden))).StatusCode);
        Assert.Equal(ruta.Revision, (await LeerAsync(client, ruta.Id)).Revision);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden, true))).StatusCode);
    }

    [Fact]
    public async Task Omisiones_duplicados_y_paradas_ajenas_se_rechazan_sin_escritura()
    {
        using var client = Cliente(); var ruta = await CrearAsync(client);
        var p = ruta.Paradas[0].Id;
        foreach (Guid[] orden in new Guid[][] { [p], [p, p], [p, Guid.NewGuid()] })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden, true))).StatusCode);
        Assert.Equal(ruta.Revision, (await LeerAsync(client, ruta.Id)).Revision);
    }

    [Fact]
    public async Task Dos_confirmaciones_con_la_misma_revision_no_se_sobrescriben()
    {
        using var client = Cliente(); using var otro = Cliente(); var ruta = await CrearAsync(client);
        var orden = ruta.Paradas.Select(p => p.Id).Reverse().ToArray();
        var respuestas = await Task.WhenAll(client.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden)),
            otro.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden)));
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(ruta.Revision + 1, (await LeerAsync(client, ruta.Id)).Revision);
    }

    [Theory]
    [InlineData("Despachada")]
    [InlineData("EnCurso")]
    [InlineData("Finalizada")]
    public async Task Rutas_no_planificadas_no_permiten_reordenar(string estado)
    {
        using var client = Cliente(); var ruta = await CrearAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            var entidad = await db.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == ruta.Id);
            entidad.Despachar(DateTimeOffset.UtcNow);
            if (estado == "EnCurso") entidad.Iniciar();
            if (estado == "Finalizada") entidad.Finalizar();
            await db.SaveChangesAsync();
        }
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, ruta.Paradas.Select(p => p.Id).Reverse().ToArray(), true))).StatusCode);
            Assert.DoesNotContain("Ordenar paradas</a>", await client.GetStringAsync($"/backoffice/rutas/{ruta.Id}"));
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            var entidad = await db.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == ruta.Id);
            if (entidad.Estado != EstadoRuta.Finalizada) { entidad.Finalizar(); await db.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task Pantalla_propone_mueve_y_guarda_solo_al_confirmar()
    {
        using var client = Cliente(); var ruta = await CrearAsync(client);
        var pagina = $"/backoffice/rutas/{ruta.Id}/orden";
        var token = await Sesion.TokenAsync(client, pagina);
        var inicial = await client.GetStringAsync(pagina);
        Assert.Contains("Confirmar y guardar orden", inicial);
        var proponer = await client.PostAsync(pagina + "?handler=Proponer", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = token, ["RevisionEsperada"] = ruta.Revision.ToString() }));
        proponer.EnsureSuccessStatusCode();
        var html = await proponer.Content.ReadAsStringAsync();
        Assert.True(html.IndexOf($"data-parada-orden=\"{ruta.Paradas[1].Id}\"", StringComparison.Ordinal)
            < html.IndexOf($"data-parada-orden=\"{ruta.Paradas[0].Id}\"", StringComparison.Ordinal));
        var orden = ruta.Paradas.Select(p => p.Id).Reverse().ToArray();
        var campos = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token), new("RevisionEsperada", ruta.Revision.ToString()) };
        campos.AddRange(orden.Select(p => new KeyValuePair<string, string>("ParadaIds", p.ToString())));
        campos.Add(new("Movimiento", $"{orden[1]}:1"));
        var mover = await client.PostAsync(pagina + "?handler=Mover", new FormUrlEncodedContent(campos));
        mover.EnsureSuccessStatusCode();
        Assert.Contains("El orden puede incumplir franjas", WebUtility.HtmlDecode(await mover.Content.ReadAsStringAsync()));
        Assert.Equal(ruta.Revision, (await LeerAsync(client, ruta.Id)).Revision);
        campos.RemoveAll(c => c.Key == "Movimiento"); // Guardar la propuesta automática, sin advertencias.
        var guardar = await client.PostAsync(pagina + "?handler=Guardar", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.Redirect, guardar.StatusCode);
        Assert.Equal(orden, (await LeerAsync(client, ruta.Id)).Paradas.Select(p => p.Id));
    }

    [Fact]
    public async Task Autenticacion_perfil_tenant_y_antiforgery_protegen_la_operacion()
    {
        using var preparacion = Cliente(); var ruta = await CrearAsync(preparacion);
        var orden = ruta.Paradas.Select(p => p.Id).Reverse().ToArray();
        HttpClient Seguro() => factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "false")).CreateClient(Sesion.Opciones);
        using var anonimo = Seguro();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Url(ruta))).StatusCode);
        using var deposito = Seguro(); await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await deposito.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden))).StatusCode);
        using var este = Seguro(); await Sesion.IngresarAsync(este, "despacho@envios-del-este.uy");
        Assert.Equal(HttpStatusCode.NotFound, (await este.GetAsync(Url(ruta))).StatusCode);
        using var demo = Seguro(); await Sesion.IngresarAsync(demo, "despacho@operador-demo.uy");
        Assert.Equal(HttpStatusCode.BadRequest, (await demo.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden))).StatusCode);
        demo.DefaultRequestHeaders.Add("RequestVerificationToken", await Sesion.TokenAsync(demo, $"/backoffice/rutas/{ruta.Id}/orden"));
        Assert.Equal(HttpStatusCode.OK, (await demo.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden))).StatusCode);
    }

    [Fact]
    public async Task Confirmacion_obsoleta_en_pantalla_recarga_el_orden_y_revision_actuales()
    {
        using var navegador = Cliente(); using var otro = Cliente(); var ruta = await CrearAsync(otro);
        var pagina = $"/backoffice/rutas/{ruta.Id}/orden";
        var token = await Sesion.TokenAsync(navegador, pagina);
        var orden = ruta.Paradas.Select(p => p.Id).Reverse().ToArray();
        (await otro.PostAsJsonAsync(Url(ruta), new GuardarOrdenParadasRequest(ruta.Revision, orden))).EnsureSuccessStatusCode();
        var campos = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token), new("RevisionEsperada", ruta.Revision.ToString()), new("ConfirmarAdvertencias", "true") };
        campos.AddRange(ruta.Paradas.Select(p => new KeyValuePair<string, string>("ParadaIds", p.Id.ToString())));
        var response = await navegador.PostAsync(pagina + "?handler=Guardar", new FormUrlEncodedContent(campos));
        response.EnsureSuccessStatusCode();
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Otra operación modificó la ruta", html);
        Assert.Contains($"value=\"{ruta.Revision + 1}\"", html);
        Assert.Equal(orden, (await LeerAsync(otro, ruta.Id)).Paradas.Select(p => p.Id));
    }

    [Fact]
    public async Task Ruta_vacia_y_revision_invalida_no_se_pueden_ordenar()
    {
        using var client = Cliente(); var ruta = await CrearAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url(ruta) + "/propuesta", new ProponerOrdenParadasRequest(0))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            var entidad = await db.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == ruta.Id);
            foreach (var envio in entidad.EnvioIds.ToArray()) entidad.QuitarEnvio(envio);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Url(ruta))).StatusCode);
    }

    private async Task<OrdenParadasResponse> ProponerAsync(HttpClient client, RutaResponse ruta)
    {
        var r = await client.PostAsJsonAsync(Url(ruta) + "/propuesta", new ProponerOrdenParadasRequest(ruta.Revision));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<OrdenParadasResponse>())!;
    }
    private static async Task<RutaResponse> LeerAsync(HttpClient client, Guid id)
        => (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{id}"))!;

    private async Task<RutaResponse> CrearAsync(HttpClient client)
    {
        var fecha = new DateOnly(2048, 1, 1).AddDays(Interlocked.Increment(ref dia));
        var direccion = Guid.NewGuid().ToString("N");
        async Task<CrearEnvioResponse> Recibir(string numero)
        {
            var r = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(new DestinatarioRequest("Ana CU42", "099123456", null, null),
                new DireccionRequest("Calle CU42", numero, "Montevideo", "Montevideo", "11300", null), [new BultoRequest(2, 30, 20, 10)]));
            r.EnsureSuccessStatusCode();
            var e = (await r.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
            (await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest(e.Numero + "-1"))).EnsureSuccessStatusCode();
            return e;
        }
        var a = await Recibir(direccion); var b = await Recibir(direccion); var c = await Recibir(Guid.NewGuid().ToString("N"));
        using (var scope = factory.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            var operador = DatosIniciales.OperadorDemoId;
            var zona = Guid.Parse("61111111-1111-1111-1111-111111111111");
            var manana = FranjaHoraria.Crear(operador, zona, new(9, 0), new(12, 0), Enum.GetValues<DayOfWeek>(), fecha);
            var tarde = FranjaHoraria.Crear(operador, zona, new(14, 0), new(18, 0), Enum.GetValues<DayOfWeek>(), fecha);
            admin.Franjas.AddRange(manana, tarde); await admin.SaveChangesAsync();
            var envios = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            foreach (var id in new[] { a.Id, b.Id, c.Id })
                (await envios.Envios.SingleAsync(e => e.Id == id)).DefinirPlanificacion(zona, id == c.Id ? manana.Id : tarde.Id, fecha);
            await envios.SaveChangesAsync();
        }
        var creada = await client.PostAsJsonAsync("/api/planificacion/rutas", new CrearRutaRequest(fecha,
            Guid.Parse("41111111-1111-1111-1111-111111111111"), Guid.Parse("51111111-1111-1111-1111-111111111111"), [a.Id, b.Id, c.Id]));
        Assert.True(creada.IsSuccessStatusCode, await creada.Content.ReadAsStringAsync());
        return (await creada.Content.ReadFromJsonAsync<RutaResponse>())!;
    }
}
