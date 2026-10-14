using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests;

public class BackofficeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task La_api_sirve_el_backoffice_con_su_layout()
    {
        // Verificación del layout; la entrada sin sesión se prueba por separado con el respaldo desactivado.
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "true"));
        using var client = host.CreateClient();

        var response = await client.GetAsync("/backoffice");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Inicio - Backoffice</title>", html);
    }
}
