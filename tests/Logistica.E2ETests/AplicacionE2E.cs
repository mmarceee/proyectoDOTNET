using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace Logistica.E2ETests;

// La aplicación completa, como la usa una persona: la API escuchando en un puerto real (Kestrel), con el Backoffice y
// las aplicaciones Blazor, sobre un PostgreSQL propio con las migraciones y los datos iniciales. Un Chromium sin
// ventana la recorre. Se arranca una sola vez para todas las pruebas de la colección.
public sealed class AplicacionE2E : IAsyncLifetime
{
    // La contraseña de los usuarios de demostración en esta corrida. Se fija acá para no depender del código interno
    // de Administración (estas pruebas miran la aplicación sólo desde afuera).
    public const string Contrasena = "E2e-Prueba-2026!";

    // Misma versión de PostgreSQL que docker-compose.yml.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6").Build();
    private ServidorE2E? _servidor;
    private IPlaywright? _playwright;
    private IBrowser? _navegador;

    // La dirección de la aplicación, por ejemplo http://localhost:51234.
    public string Url { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _servidor = new ServidorE2E(_postgres.GetConnectionString());
        _servidor.UseKestrel(0); // puerto libre elegido por el sistema
        _servidor.StartServer();  // migra la base y carga los datos iniciales

        // "localhost" y no 127.0.0.1: Chromium sólo acepta la cookie de sesión (Secure) sin HTTPS en localhost.
        var direccion = new Uri(_servidor.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First());
        Url = $"http://localhost:{direccion.Port}";

        _playwright = await Playwright.CreateAsync();
        _navegador = await _playwright.Chromium.LaunchAsync();
    }

    // Cada usuario de una prueba usa su propio contexto: es como una ventana de incógnito, con cookies y almacenamiento
    // propios. Con E2E_RESULTADOS (el CI y scripts/e2e-en-docker.sh), cada ventana graba su traza: capturas y pasos
    // que se abren en https://trace.playwright.dev para ver qué hizo el robot y dónde se trabó.
    public async Task<IPage> NuevaPaginaAsync([CallerMemberName] string quien = "")
    {
        var contexto = await _navegador!.NewContextAsync(new()
        {
            BaseURL = Url,
            Locale = "es-UY",
            TimezoneId = "America/Montevideo",
        });
        contexto.SetDefaultTimeout(15_000);
        if (CarpetaTrazas is not null)
        {
            await contexto.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Title = quien });
            _trazas.Add((contexto, $"{_trazas.Count + 1:D2}-{quien}.zip"));
        }

        return await contexto.NewPageAsync();
    }

    private static readonly string? CarpetaTrazas = Environment.GetEnvironmentVariable("E2E_RESULTADOS");
    private readonly List<(IBrowserContext Contexto, string Archivo)> _trazas = [];

    public async Task DisposeAsync()
    {
        foreach (var (contexto, archivo) in _trazas)
        {
            await contexto.Tracing.StopAsync(new() { Path = Path.Combine(CarpetaTrazas!, "trazas", archivo) });
        }

        if (_navegador is not null)
        {
            await _navegador.CloseAsync();
        }

        _playwright?.Dispose();
        if (_servidor is not null)
        {
            await _servidor.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    // La configuración que cambia respecto del desarrollo: la base de esta corrida, sin Valkey ni RabbitMQ (la caché
    // queda en memoria y el Outbox no publica) y con la sesión real, sin el inquilino provisorio.
    private sealed class ServidorE2E(string conexion) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Postgres", conexion);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("ConnectionStrings:RabbitMQ", "");
            builder.UseSetting("ConnectionStrings:Valkey", "");
            builder.UseSetting("Identity:PermitirTenantProvisorio", "false");
            builder.UseSetting("CierreDiario:Habilitado", "false");
            builder.UseSetting("Demo:Contrasena", Contrasena);
            // Sin el SQL de cada consulta: en el log quedan los avisos y los errores.
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
        }
    }
}

// Todas las clases de prueba comparten la aplicación y el navegador, y corren de a una.
[CollectionDefinition(Nombre)]
public sealed class ColeccionE2E : ICollectionFixture<AplicacionE2E>
{
    public const string Nombre = "Aplicación completa";
}
