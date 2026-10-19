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
        // Los casos de mensajería habilitan su propio broker aislado; nunca usar el de user-secrets.
        builder.UseSetting("ConnectionStrings:RabbitMQ", "");
        // Tampoco el Valkey de user-secrets: cada clase tiene su base, y una caché compartida devolvería datos de otra
        // corrida (y ensuciaría la de desarrollo). Sin cadena de conexión, la caché es la memoria de esta API.
        builder.UseSetting("ConnectionStrings:Valkey", "");
        // Las pruebas históricas preparan datos sin sesión; las de seguridad desactivan este respaldo.
        builder.UseSetting("Identity:PermitirTenantProvisorio", "true");
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
