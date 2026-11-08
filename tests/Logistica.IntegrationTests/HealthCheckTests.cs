using System.Net;

namespace Logistica.IntegrationTests;

// /health chequea PostgreSQL (PostgresHealthCheck). La base caída se prueba en Resiliencia/PostgresCaidoTests.
public class HealthCheckTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task La_api_responde_en_health()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
