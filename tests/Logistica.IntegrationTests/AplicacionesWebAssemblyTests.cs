using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests;

public class AplicacionesWebAssemblyTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/portal/")]
    [InlineData("/portal/envios/nuevo")]
    [InlineData("/seguimiento/")]
    [InlineData("/seguimiento/token-de-ejemplo")]
    [InlineData("/repartidor/")]
    public async Task Cada_ruta_de_una_aplicacion_devuelve_su_index_html(string path)
    {
        var app = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];

        // Este fixture verifica assets y rutas, no autenticación (cubierta con cookie real en PlanificacionIdentityTests).
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "true"));
        using var client = host.CreateClient();
        var html = await client.GetStringAsync(path);

        Assert.Contains($"<base href=\"/{app}/\" />", html);
    }

    [Theory]
    [InlineData("portal")]
    [InlineData("seguimiento")]
    [InlineData("repartidor")]
    public async Task El_runtime_de_Blazor_se_sirve_bajo_la_ruta_de_la_aplicacion(string app)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "true"));
        using var client = host.CreateClient();
        var html = await client.GetStringAsync($"/{app}/");

        // El index.html referencia el script de Blazor con su nombre final (con huella de contenido).
        var script = Regex.Match(html, @"src=""(_framework/blazor\.webassembly[^""]*\.js)""").Groups[1].Value;
        var response = await client.GetAsync($"/{app}/{script}");

        Assert.NotEmpty(script);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task El_service_worker_del_repartidor_se_sirve_en_su_ruta()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "true"));
        using var client = host.CreateClient();
        var response = await client.GetAsync("/repartidor/service-worker.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task El_ingreso_del_repartidor_enlaza_recursos_de_instalacion_publicos()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        const string rutaManifiesto = "/repartidor/manifest.webmanifest";
        var ingreso = await client.GetStringAsync("/repartidor/ingresar");
        Assert.Contains($"rel=\"manifest\" href=\"{rutaManifiesto}\"", ingreso);

        using var respuesta = await client.GetAsync(rutaManifiesto);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var manifiesto = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var iconos = manifiesto.RootElement.GetProperty("icons").EnumerateArray().ToArray();
        Assert.Equal(2, iconos.Length);
        foreach (var icono in iconos)
        {
            using var recurso = await client.GetAsync($"/repartidor/{icono.GetProperty("src").GetString()}");
            Assert.Equal(HttpStatusCode.OK, recurso.StatusCode);
            Assert.Equal("image/png", recurso.Content.Headers.ContentType?.MediaType);
        }

        using var app = await client.GetAsync("/repartidor/");
        Assert.Equal(HttpStatusCode.Redirect, app.StatusCode);
        Assert.Equal("/repartidor/ingresar", app.Headers.Location?.OriginalString);
        using var worker = await client.GetAsync("/repartidor/service-worker.js");
        Assert.Equal(HttpStatusCode.Redirect, worker.StatusCode);
    }

    [Theory]
    [InlineData("portal")]
    [InlineData("seguimiento")]
    [InlineData("repartidor")]
    public async Task Los_archivos_propios_referenciados_por_el_arranque_existen_y_el_arranque_no_se_cachea(string app)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "true"));
        using var client = host.CreateClient();
        using var respuesta = await client.GetAsync($"/{app}/_framework/dotnet.js");
        respuesta.EnsureSuccessStatusCode();
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
        var arranque = await respuesta.Content.ReadAsStringAsync();
        var archivos = Regex.Matches(arranque, @"Logistica\.[A-Za-z0-9_.-]+\.wasm")
            .Select(m => m.Value).Distinct().ToArray();
        Assert.NotEmpty(archivos);
        foreach (var archivo in archivos)
        {
            using var recurso = await client.GetAsync($"/{app}/_framework/{archivo}");
            Assert.True(recurso.StatusCode == HttpStatusCode.OK, $"El arranque de {app} referencia {archivo}, que devuelve {recurso.StatusCode}.");
            Assert.Equal("application/wasm", recurso.Content.Headers.ContentType?.MediaType);
        }
    }
}
