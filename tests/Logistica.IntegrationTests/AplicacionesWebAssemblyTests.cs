using System.Net;
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
