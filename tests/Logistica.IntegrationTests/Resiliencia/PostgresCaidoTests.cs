using System.Diagnostics;
using System.Net;
using DotNet.Testcontainers.Builders;
using Xunit.Abstractions;
using static Logistica.IntegrationTests.Resiliencia.Interrupciones;

namespace Logistica.IntegrationTests.Resiliencia;

// Caso 3 de docs/pruebas-de-resiliencia.md: PostgreSQL detenido con la API atendiendo. Sin base no se puede crear un
// envío, pero la API responde enseguida un 503 con Retry-After (no un 500 ni un request colgado), /health informa
// Unhealthy, y cuando la base vuelve todo sigue funcionando sin reiniciar la API. Usa un PostgreSQL propio, no el
// compartido por la suite, para poder detenerlo.
[Collection(ContenedoresPropios.Nombre)]
public sealed class PostgresCaidoTests(ITestOutputHelper salida) : IAsyncLifetime
{
    private ContenedorInterrumpible postgres = null!;

    public async Task InitializeAsync()
    {
        postgres = await ContenedorInterrumpible.IniciarAsync("postgres:17.6", [5432], b => b
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "logistica")
            // Por TCP: mientras se inicializa, PostgreSQL sólo escucha en el socket local.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-h", "127.0.0.1", "-U", "postgres")));
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task Con_la_base_detenida_responde_503_enseguida_y_se_recupera_sola_cuando_vuelve()
    {
        await using var api = new PostgresApiFactory(
            $"Host={postgres.Host};Port={postgres.Puerto(5432)};Database=logistica;Username=postgres;Password=postgres");
        using var portal = await PortalAsync(api);
        using var salud = api.CreateClient();

        // Antes: todo funciona.
        Assert.Equal(HttpStatusCode.Created, (await CrearEnvioAsync(portal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await salud.GetAsync("/health")).StatusCode);

        await postgres.DetenerAsync();

        // Durante: el request no se cuelga y el cliente sabe que puede reintentar.
        var reloj = Stopwatch.StartNew();
        var sinBase = await CrearEnvioAsync(portal);
        reloj.Stop();
        salida.WriteLine($"Crear un envío sin base respondió {(int)sinBase.StatusCode} en {reloj.ElapsedMilliseconds} ms.");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, sinBase.StatusCode);
        Assert.Equal("5", sinBase.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        Assert.Equal("application/problem+json", sinBase.Content.Headers.ContentType?.MediaType);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(5), $"El request sin base tardó {reloj.Elapsed}.");
        var saludSinBase = await salud.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, saludSinBase.StatusCode);
        Assert.Equal("Unhealthy", await saludSinBase.Content.ReadAsStringAsync());

        await postgres.ReanudarAsync();
        reloj.Restart();
        var rechazados = 0;

        // Después: la misma API, sin reiniciar, vuelve a crear envíos. Las conexiones que quedaron en el pool se
        // descartan al fallar, así que algún request puede recibir todavía un 503 antes de que todo se normalice.
        await EsperarAsync(async () => (await salud.GetAsync("/health")).IsSuccessStatusCode ? "ok" : null,
            TimeSpan.FromSeconds(30), "/health vuelve a Healthy");
        salida.WriteLine($"/health volvió a Healthy {reloj.Elapsed.TotalSeconds:0.0} s después de que la base aceptó conexiones.");
        var creado = await EsperarAsync(async () =>
        {
            var respuesta = await CrearEnvioAsync(portal);
            if (respuesta.StatusCode != HttpStatusCode.Created) rechazados++;
            return respuesta.StatusCode == HttpStatusCode.Created ? respuesta : null;
        }, TimeSpan.FromSeconds(30), "se vuelve a crear un envío");
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        salida.WriteLine($"Se volvió a crear un envío {reloj.Elapsed.TotalSeconds:0.0} s después; antes hubo {rechazados} respuestas no exitosas.");
    }
}
