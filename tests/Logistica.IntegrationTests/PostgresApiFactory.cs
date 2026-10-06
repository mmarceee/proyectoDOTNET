using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Logistica.IntegrationTests;

// La API completa en memoria, conectada a un PostgreSQL real que Testcontainers levanta en Docker
// para cada clase de prueba y descarta al terminar. Misma versión de PostgreSQL que docker-compose.yml.
public sealed class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Se aplican antes de que Program.cs lea la configuración, así la API usa este contenedor
        // y crea las tablas al iniciar, igual que en docker compose.
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }

    public Task InitializeAsync()
    {
        return _postgres.StartAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return _postgres.DisposeAsync().AsTask();
    }
}
