using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

public class PlanificacionIdentityTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int _dia;
    private HttpClient Cliente(bool desarrollo = true) => factory.WithWebHostBuilder(b => b
        .UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", desarrollo.ToString())
        .UseSetting("Identity:PermitirTenantProvisorio", "true")).CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    [Fact]
    public async Task Login_real_permite_CU40_CU41_CU43_y_registra_el_responsable()
    {
        using var client = Cliente();
        var a = await RecibidoAsync(client); var b = await RecibidoAsync(client);
        await IngresarAsync(client, "despacho@operador-demo.uy");
        var ruta = await CrearAsync(client, a.Id, b.Id);
        var detalle = await client.GetStringAsync($"/backoffice/rutas/{ruta.Id}");
        Assert.DoesNotContain("Modo de desarrollo sin sesión", detalle);
        var quitar = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            $"/api/planificacion/rutas/{ruta.Id}/envios/{b.Id}")
            { Content = JsonContent.Create(new QuitarEnvioDeRutaRequest(ruta.Revision)) });
        quitar.EnsureSuccessStatusCode();
        var actual = (await quitar.Content.ReadFromJsonAsync<RutaResponse>())!;
        var despacho = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho",
            new DespacharRutaRequest(actual.Revision));
        despacho.EnsureSuccessStatusCode();
        Assert.Equal("Despachada", (await despacho.Content.ReadFromJsonAsync<RutaResponse>())!.Estado);
        using var scope = factory.Services.CreateScope();
        var usuario = (await scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>()
            .FindByEmailAsync("despacho@operador-demo.uy"))!;
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var validaciones = await db.Validaciones.Where(v => v.RutaId == ruta.Id).ToListAsync();
        Assert.Equal(2, validaciones.Count);
        Assert.All(validaciones, v => Assert.Equal(usuario.Id, v.ResponsableId));
        Assert.Equal(usuario.Id, (await db.Retiros.SingleAsync(r => r.RutaId == ruta.Id)).ResponsableId);
        var mensaje = Assert.Single(await db.Outbox.Where(m => m.Tipo == "RutaDespachada.v1").ToListAsync());
        Assert.Equal(usuario.OperadorId, mensaje.OperadorId);
        using var contenido = JsonDocument.Parse(mensaje.Contenido);
        Assert.Equal(usuario.Id, contenido.RootElement.GetProperty("ResponsableId").GetGuid());
        var entidad = await db.Rutas.SingleAsync(r => r.Id == ruta.Id);
        entidad.Finalizar(); await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cookie_de_Este_no_usa_el_operador_provisorio_Demo_y_no_modifica_sus_rutas()
    {
        using var demo = Cliente();
        var envio = await RecibidoAsync(demo);
        await IngresarAsync(demo, "despacho@operador-demo.uy");
        var ruta = await CrearAsync(demo, envio.Id);
        using var este = Cliente();
        await IngresarAsync(este, "despacho@envios-del-este.uy");
        var rutas = (await este.GetFromJsonAsync<List<RutaResponse>>("/api/planificacion/rutas"))!;
        Assert.DoesNotContain(rutas, r => r.Id == ruta.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await este.GetAsync($"/api/planificacion/rutas/{ruta.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await este.PutAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/planificacion",
            new ModificarRutaRequest(ruta.Revision, ruta.Fecha, ruta.RepartidorId, ruta.VehiculoId))).StatusCode);
        var quitar = await este.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            $"/api/planificacion/rutas/{ruta.Id}/envios/{envio.Id}")
            { Content = JsonContent.Create(new QuitarEnvioDeRutaRequest(ruta.Revision)) });
        Assert.Equal(HttpStatusCode.NotFound, quitar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await este.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho",
            new DespacharRutaRequest(ruta.Revision))).StatusCode);
        Assert.Equal(ruta.Revision, (await demo.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!.Revision);
        Assert.DoesNotContain("Modo de desarrollo sin sesión", await este.GetStringAsync("/backoffice/rutas"));
    }

    [Theory]
    [InlineData("admin@operador-demo.uy")]
    [InlineData("admin@envios-del-este.uy")]
    [InlineData("comercio@tiendademo.uy")]
    public async Task Sesion_sin_rol_despachador_se_rechaza_aun_con_respaldo_de_desarrollo(string email)
    {
        using var client = Cliente();
        await IngresarAsync(client, email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/planificacion/rutas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/backoffice/rutas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/planificacion/rutas",
            new CrearRutaRequest(new(2040, 1, 1), Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/planificacion/rutas/{Guid.NewGuid()}/despacho",
            new DespacharRutaRequest(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            $"/api/planificacion/rutas/{Guid.NewGuid()}/envios/{Guid.NewGuid()}")
            { Content = JsonContent.Create(new QuitarEnvioDeRutaRequest(1)) })).StatusCode);
    }

    [Fact]
    public async Task Sin_respaldo_Backoffice_redirige_a_login_y_API_responde_401()
    {
        using var client = Cliente(false);
        var pagina = await client.GetAsync("/backoffice/rutas");
        Assert.Equal(HttpStatusCode.Redirect, pagina.StatusCode);
        Assert.Contains("/cuenta/ingresar", pagina.Headers.Location!.ToString());
        Assert.Contains("ReturnUrl", pagina.Headers.Location.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/planificacion/rutas")).StatusCode);
    }

    [Fact]
    public async Task Cookie_autenticada_no_permite_escrituras_sin_antiforgery()
    {
        using var client = Cliente();
        await IngresarAsync(client, "despacho@operador-demo.uy");
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/planificacion/rutas",
            new CrearRutaRequest(new(2040, 1, 1), Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]))).StatusCode);
    }

    [Theory]
    [InlineData("/backoffice")]
    [InlineData("/backoffice/rutas")]
    [InlineData("/portal")]
    [InlineData("/portal/index.html")]
    public async Task Entradas_privadas_exigen_login_en_Development(string path)
    {
        using var client = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "false"))
            .CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var portal = path.StartsWith("/portal");
        Assert.Contains(portal ? "/portal/ingresar" : "/cuenta/ingresar", response.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/cuenta/ingresar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/seguimiento/")).StatusCode);
        await IngresarAsync(client, portal ? "comercio@tiendademo.uy" : "despacho@operador-demo.uy");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Boton_cerrar_sesion_elimina_cookie_y_vuelve_a_proteger_ambas_aplicaciones()
    {
        using var client = factory.WithWebHostBuilder(b => b
            .UseSetting("Identity:PermitirTenantProvisorio", "false"))
            .CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await IngresarAsync(client, "despacho@operador-demo.uy");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/backoffice"));
        Assert.Contains("despacho@operador-demo.uy", html);
        Assert.Contains("Cerrar sesión", html);
        Assert.Contains("action=\"/cuenta/salir\"", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/cuenta/salir", new FormUrlEncodedContent(
            new Dictionary<string, string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/backoffice")).StatusCode);
        var salir = await client.PostAsync("/cuenta/salir", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = token.Groups[1].Value }));
        Assert.Equal(HttpStatusCode.Redirect, salir.StatusCode);
        Assert.Equal("/cuenta/ingresar", salir.Headers.Location!.ToString());
        foreach (var path in new[] { "/backoffice", "/portal" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(path == "/portal" ? "/portal/ingresar" : "/cuenta/ingresar", response.Headers.Location!.ToString());
        }
    }

    [Theory]
    [InlineData("/backoffice/rutas/01a1235e-48c1-7014-beeb-3e28bca46c21")]
    [InlineData("/backoffice/envios/ENV-DEMO-1?tab=historial")]
    [InlineData("/backoffice/rutas?Estado=Despachada")]
    [InlineData("https://example.com/")]
    public async Task Login_siempre_vuelve_al_inicio(string retorno)
    {
        using var client = Cliente();
        var response = await IngresarAsync(client, "despacho@envios-del-este.uy", retorno);
        Assert.Equal("/backoffice", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Comercio_ingresa_al_Portal_y_no_puede_abrir_Backoffice_ni_recibir_bultos()
    {
        using var client = Cliente();
        var login = await IngresarAsync(client, "comercio@tiendademo.uy");
        // CU-03: el comercio primero elige el operador de la sesión; después sigue al Portal.
        Assert.Equal("/cuenta/operador?ReturnUrl=%2Fportal%2F", login.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/portal/")).StatusCode);
        foreach (var path in new[] { "/backoffice", "/backoffice/envios", "/backoffice/deposito/recepcion", "/repartidor/" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/deposito/recepciones",
            new RecibirBultoRequest("ENV-PRUEBA-1", null))).StatusCode);
        var pantalla = await client.GetStringAsync("/portal/ingresar");
        Assert.Contains("Ingresar al Portal", pantalla);
        Assert.DoesNotContain("href=\"/backoffice/envios\"", pantalla);
        Assert.DoesNotContain("href=\"/backoffice/deposito/recepcion\"", pantalla);
    }

    [Theory]
    [InlineData("/cuenta/ingresar", "comercio@tiendademo.uy")]
    [InlineData("/portal/ingresar", "despacho@operador-demo.uy")]
    [InlineData("/repartidor/ingresar", "despacho@operador-demo.uy")]
    [InlineData("/repartidor/ingresar", "comercio@tiendademo.uy")]
    [InlineData("/portal/ingresar", "repartidor@operador-demo.uy")]
    [InlineData("/cuenta/ingresar", "repartidor@operador-demo.uy")]
    public async Task Login_rechaza_cuentas_de_la_otra_aplicacion(string path, string email)
    {
        using var client = Cliente();
        var token = await TokenAsync(client, path);
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Contrasena"] = UsuariosIniciales.ContrasenaDemo,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("El correo o la contraseña no son correctos", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.StartsWith("logistica.sesion=")));
    }

    [Fact]
    public async Task Personal_no_puede_abrir_el_Portal_con_su_cookie()
    {
        using var client = Cliente();
        await IngresarAsync(client, "despacho@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/portal/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/portal/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/repartidor/")).StatusCode);
    }

    [Fact]
    public async Task Portal_cierra_sesion_y_regresa_a_su_propio_login()
    {
        using var client = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
            .CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await IngresarAsync(client, "comercio@tiendademo.uy");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/cuenta/salir?portal=true"));
        Assert.Contains("Portal del comercio", html);
        Assert.DoesNotContain("href=\"/backoffice/envios\"", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        // GET sólo muestra la confirmación, no cierra la sesión.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/portal/")).StatusCode);
        var salir = await client.PostAsync("/cuenta/salir?portal=true", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Portal"] = "true", ["__RequestVerificationToken"] = token.Groups[1].Value }));
        Assert.Equal("/portal/ingresar", salir.Headers.Location!.ToString());
        var entrada = await client.GetAsync("/portal/");
        Assert.Equal(HttpStatusCode.Redirect, entrada.StatusCode);
        Assert.Equal("/portal/ingresar", entrada.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("repartidor@operador-demo.uy")]
    [InlineData("repartidor@envios-del-este.uy")]
    public async Task Repartidor_tiene_login_propio_y_no_accede_a_las_otras_aplicaciones(string email)
    {
        using var client = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
            .CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var entrada = await client.GetAsync("/repartidor/");
        Assert.Equal(HttpStatusCode.Redirect, entrada.StatusCode);
        Assert.Equal("/repartidor/ingresar", entrada.Headers.Location!.ToString());
        var html = await client.GetStringAsync("/repartidor/ingresar");
        Assert.Contains("Ingresar como repartidor", html);
        Assert.DoesNotContain("href=\"/backoffice/envios\"", html);
        var login = await IngresarAsync(client, email, "/backoffice/rutas");
        Assert.Equal("/repartidor/", login.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/repartidor/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/repartidor/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/repartidor/service-worker.js")).StatusCode);
        foreach (var path in new[] { "/backoffice", "/portal/", "/backoffice/deposito/recepcion" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/deposito/recepciones",
            new RecibirBultoRequest("ENV-PRUEBA-1", null))).StatusCode);
        var salida = WebUtility.HtmlDecode(await client.GetStringAsync("/cuenta/salir?repartidor=true"));
        var token = Regex.Match(salida, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var logout = await client.PostAsync("/cuenta/salir?repartidor=true", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Repartidor"] = "true", ["__RequestVerificationToken"] = token.Groups[1].Value }));
        Assert.Equal("/repartidor/ingresar", logout.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/repartidor/")).StatusCode);
    }

    private static async Task<HttpResponseMessage> IngresarAsync(HttpClient client, string email, string? retorno = null)
    {
        var login = email.StartsWith("repartidor@") ? "/repartidor/ingresar"
            : email == "comercio@tiendademo.uy" ? "/portal/ingresar" : "/cuenta/ingresar";
        var token = await TokenAsync(client, login);
        var response = await client.PostAsync(login, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Contrasena"] = UsuariosIniciales.ContrasenaDemo,
            ["ReturnUrl"] = retorno ?? "",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("logistica.sesion="));
        // El token anónimo anterior no sirve para la identidad autenticada.
        client.DefaultRequestHeaders.Add("RequestVerificationToken", await TokenAsync(client, login));
        return response;
    }
    private static async Task<string> TokenAsync(HttpClient client, string login = "/cuenta/ingresar")
    {
        var html = await client.GetStringAsync(login);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }
    private static async Task<RutaResponse> CrearAsync(HttpClient client, params Guid[] ids)
    {
        var fecha = new DateOnly(2040, 1, 1).AddDays(Interlocked.Increment(ref _dia));
        var response = await client.PostAsJsonAsync("/api/planificacion/rutas", new CrearRutaRequest(fecha,
            Guid.Parse("41111111-1111-1111-1111-111111111111"), Guid.Parse("51111111-1111-1111-1111-111111111111"), ids));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RutaResponse>())!;
    }
    private static async Task<CrearEnvioResponse> RecibidoAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Prueba Identity", "099123456", null, null),
            new DireccionRequest("Av. Italia", Guid.NewGuid().ToString("N"), "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        response.EnsureSuccessStatusCode();
        var envio = (await response.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        (await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest(envio.Numero + "-1", null))).EnsureSuccessStatusCode();
        return envio;
    }
}
