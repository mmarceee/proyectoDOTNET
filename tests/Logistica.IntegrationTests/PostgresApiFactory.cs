using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Logistica.IntegrationTests;

// La API completa en memoria, conectada a un PostgreSQL real. Cada clase de prueba recibe una base propia, clonada de
// una plantilla que ya tiene las migraciones y los datos iniciales, así que el aislamiento por clase se mantiene.
public sealed class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string? _conexion;

    public PostgresApiFactory()
    {
    }

    // Sólo para armar la plantilla: la API arranca sobre esa base, la migra y carga los datos iniciales.
    private PostgresApiFactory(string conexion)
    {
        _conexion = conexion;
    }

    public string ConnectionString => _conexion
        ?? throw new InvalidOperationException("La base de la clase todavía no se creó.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Se aplican antes de que Program.cs lea la configuración, así la API usa la base de esta clase.
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        // El enrutamiento de miles de assets se comprueba en AplicacionesWebAssemblyTests con el catálogo real.
        // Repetirlo en cada API efímera de negocio retiene demasiada memoria durante la suite con cobertura.
        builder.UseSetting("StaticAssets:ManifestPath", Path.Combine(AppContext.BaseDirectory, "staticassets.vacio.json"));
        // Sobre una base clonada no hay migraciones pendientes y los datos iniciales ya están: sólo se comprueba.
        builder.UseSetting("Database:MigrateOnStartup", "true");
        // Los casos de mensajería habilitan su propio broker aislado; nunca usar el de user-secrets.
        builder.UseSetting("ConnectionStrings:RabbitMQ", "");
        // Tampoco el Valkey de user-secrets: cada clase tiene su base, y una caché compartida devolvería datos de otra
        // corrida (y ensuciaría la de desarrollo). Sin cadena de conexión, la caché es la memoria de esta API.
        builder.UseSetting("ConnectionStrings:Valkey", "");
        // Las pruebas históricas preparan datos sin sesión; las de seguridad desactivan este respaldo.
        builder.UseSetting("Identity:PermitirTenantProvisorio", "true");
    }

    public async Task InitializeAsync()
    {
        _conexion = await PostgresCompartido.CrearBaseAsync(async conexion =>
        {
            await using var plantilla = new PostgresApiFactory(conexion);
            using var client = plantilla.CreateClient(); // arranca la API: migraciones y datos iniciales
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Detener primero los hosts y sus servicios de fondo, incluidos los creados con WithWebHostBuilder.
        // La base queda en el contenedor, que se descarta al terminar la suite.
        await base.DisposeAsync();
    }
}

// Un solo contenedor de PostgreSQL para toda la suite, en lugar de uno por clase: arrancarlo y migrar los siete esquemas
// llevaba unos 8 s por clase. Las bases se clonan de a una, porque PostgreSQL no copia una plantilla en uso.
internal static class PostgresCompartido
{
    private const string Plantilla = "plantilla";

    // Misma versión de PostgreSQL que docker-compose.yml. Con las clases en paralelo, cada API abre su propio pool.
    private static readonly PostgreSqlContainer Contenedor = new PostgreSqlBuilder("postgres:17.6")
        .WithCommand("-c", "max_connections=300")
        .Build();

    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static bool _plantillaLista;

    public static async Task<string> CrearBaseAsync(Func<string, Task> prepararPlantilla)
    {
        await Candado.WaitAsync();
        try
        {
            if (!_plantillaLista)
            {
                await Contenedor.StartAsync();
                await EjecutarAsync($"CREATE DATABASE {Plantilla}");
                var conexionPlantilla = Conexion(Plantilla);
                await prepararPlantilla(conexionPlantilla);
                // La API ya se detuvo, pero su pool puede conservar conexiones abiertas a la plantilla.
                NpgsqlConnection.ClearPool(new NpgsqlConnection(conexionPlantilla));
                _plantillaLista = true;
            }

            var nombre = $"prueba_{Guid.NewGuid():N}";
            await EjecutarAsync($"CREATE DATABASE {nombre} TEMPLATE {Plantilla}");
            return Conexion(nombre);
        }
        finally
        {
            Candado.Release();
        }
    }

    private static string Conexion(string baseDeDatos) =>
        new NpgsqlConnectionStringBuilder(Contenedor.GetConnectionString()) { Database = baseDeDatos }.ConnectionString;

    private static async Task EjecutarAsync(string sql)
    {
        await using var conexion = new NpgsqlConnection(Contenedor.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }
}
