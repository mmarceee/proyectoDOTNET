using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

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

        var html = await factory.CreateClient().GetStringAsync(path);

        Assert.Contains($"<base href=\"/{app}/\" />", html);
    }

    [Theory]
    [InlineData("portal")]
    [InlineData("seguimiento")]
    [InlineData("repartidor")]
    public async Task El_runtime_de_Blazor_se_sirve_bajo_la_ruta_de_la_aplicacion(string app)
    {
        var client = factory.CreateClient();
        var html = await client.GetStringAsync($"/{app}/");

        // El index.html referencia el script de Blazor con su nombre final (con huella de contenido).
        var script = Regex.Match(html, @"src=""(_framework/blazor\.webassembly[^""]*\.js)""").Groups[1].Value;
        var response = await client.GetAsync($"/{app}/{script}");

        Assert.NotEmpty(script);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task El_service_worker_del_repartidor_se_sirve_en_su_ruta()
    {
        var response = await factory.CreateClient().GetAsync("/repartidor/service-worker.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
