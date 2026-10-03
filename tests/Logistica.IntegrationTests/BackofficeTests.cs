using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logistica.IntegrationTests;

public class BackofficeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task La_api_sirve_el_backoffice_con_su_layout()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/backoffice");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Inicio - Backoffice</title>", html);
    }
}
