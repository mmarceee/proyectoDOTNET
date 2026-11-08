using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using DotNet.Testcontainers.Builders;
using Logistica.BuildingBlocks.Infrastructure.Caching;
using Logistica.IntegrationTests.Identidad;
using Microsoft.AspNetCore.Hosting;
using StackExchange.Redis;
using Xunit.Abstractions;
using static Logistica.IntegrationTests.Administracion.PaginaTarifario;
using static Logistica.IntegrationTests.Resiliencia.Interrupciones;

namespace Logistica.IntegrationTests.Resiliencia;

// Caso 1 de docs/pruebas-de-resiliencia.md: Valkey detenido con la API atendiendo. Crear un envío lee el tarifario y
// las reglas vigentes, que están en la caché (ADR-0006). Sin Valkey se leen de PostgreSQL sin demorar el request y se
// cuenta cada error en logistica.cache.errores; cuando Valkey vuelve, la API se reconecta sola y vuelve a guardar.
[Collection(ContenedoresPropios.Nombre)]
public sealed class ValkeyCaidoTests(PostgresApiFactory factory, ITestOutputHelper salida) : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private ContenedorInterrumpible valkey = null!;

    private string Conexion => $"{valkey.Host}:{valkey.Puerto(6379)}";

    public async Task InitializeAsync()
    {
        valkey = await ContenedorInterrumpible.IniciarAsync("valkey/valkey:8.1", [6379], b => b
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)));
    }

    public async Task DisposeAsync() => await valkey.DisposeAsync();

    [Fact]
    public async Task Sin_Valkey_los_envios_se_crean_desde_la_base_y_al_volver_la_cache_se_usa_de_nuevo()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Valkey", Conexion));
        using var portal = await PortalAsync(api);

        // Antes: el tarifario y las reglas quedan guardados en Valkey.
        Assert.Equal(HttpStatusCode.Created, (await CrearEnvioAsync(portal)).StatusCode);
        Assert.True(await HayClavesAsync(), "La caché no guardó nada con Valkey disponible.");

        await valkey.DetenerAsync();

        // Durante: los envíos se siguen creando, sin esperar a la caché, y cada falla se cuenta.
        using var errores = new ContadorErroresCache();
        var reloj = Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.Created, (await CrearEnvioAsync(portal)).StatusCode);
        salida.WriteLine($"Primer envío sin Valkey: {reloj.ElapsedMilliseconds} ms.");
        reloj.Restart();
        var sinCache = await CrearEnvioAsync(portal);
        reloj.Stop();
        salida.WriteLine($"Segundo envío sin Valkey: {reloj.ElapsedMilliseconds} ms; errores de caché contados: {errores.Total}.");
        Assert.Equal(HttpStatusCode.Created, sinCache.StatusCode);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(3), $"Crear un envío sin Valkey tardó {reloj.Elapsed}.");
        Assert.True(errores.Total > 0, "No se contó ningún error de la caché con Valkey detenido.");

        await valkey.ReanudarAsync();
        reloj.Restart();

        // Después: se vacía Valkey (puede haber recuperado su volcado al arrancar) y, sin reiniciar la API, un envío
        // nuevo vuelve a guardar el tarifario y las reglas.
        await EsperarAsync(async () =>
        {
            try
            {
                await using var admin = await ConnectionMultiplexer.ConnectAsync($"{Conexion},allowAdmin=true");
                await admin.GetServers()[0].FlushAllDatabasesAsync();
                return "ok";
            }
            catch (RedisConnectionException)
            {
                return null;
            }
        }, TimeSpan.FromSeconds(15), "Valkey vuelve a aceptar conexiones");
        await EsperarAsync(async () =>
        {
            var respuesta = await CrearEnvioAsync(portal);
            return respuesta.StatusCode == HttpStatusCode.Created && await HayClavesAsync() ? "ok" : null;
        }, TimeSpan.FromSeconds(60), "la API vuelve a guardar en Valkey");
        salida.WriteLine($"La API volvió a guardar en Valkey {reloj.Elapsed.TotalSeconds:0.0} s después de que volvió.");
    }

    // Invalidación fallida (ADR-0006, sección 2.5): se publica una tarifa con Valkey detenido, así que el borrado del
    // puntero falla. Mientras está caído se lee de la base y el precio es el nuevo. Al volver, Valkey recupera su volcado
    // con el puntero viejo, y la API puede leerlo hasta que vence: como dura 1 minuto, nunca más de un minuto desde que
    // se guardó. Tarda poco más de un minuto, porque espera ese vencimiento.
    [Fact]
    public async Task Una_tarifa_publicada_sin_Valkey_rige_a_mas_tardar_al_minuto_de_guardado_el_puntero_viejo()
    {
        using var api = factory.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Valkey", Conexion)
            .UseSetting("Identity:PermitirTenantProvisorio", "false"));
        using var admin = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");

        // Antes: la versión 1 cobra $150 y su puntero queda en Valkey por un minuto, contado desde ahora.
        Assert.Contains("$ 150 con la versión 1", await CalcularAsync(admin, ZonaDemo, "Estandar"));
        var desdeGuardado = Stopwatch.StartNew();
        Assert.True(await HayClavesAsync(), "La caché no guardó el puntero con Valkey disponible.");

        await valkey.DetenerAsync();

        // Durante: se publica $175. Guardar en la base funciona; borrar el puntero falla y no deshace la publicación.
        await AccionAsync(admin, Pagina, "Nueva", []);
        await QuitarReglaAsync(admin, "$ 150");
        await AccionAsync(admin, Borrador, "Regla", Regla(ZonaDemo, "Estandar", "0", "5", "0", "0.1", "175"));
        var publicada = await AccionAsync(admin, Borrador, "Publicar", [new("ConfirmaSinTarifa", "true")]);
        Assert.Equal(HttpStatusCode.Redirect, publicada.StatusCode);
        Assert.Contains("$ 175 con la versión 2", await CalcularAsync(admin, ZonaDemo, "Estandar"));

        await valkey.ReanudarAsync();

        // Después: se calcula una vez por segundo hasta pasado el minuto. El precio viejo sólo puede aparecer mientras
        // el puntero recuperado no venció.
        var ultimoViejo = TimeSpan.Zero;
        while (desdeGuardado.Elapsed < TimeSpan.FromSeconds(65))
        {
            var texto = await CalcularAsync(admin, ZonaDemo, "Estandar");
            if (texto.Contains("$ 150 con la versión 1"))
            {
                ultimoViejo = desdeGuardado.Elapsed;
            }
            else
            {
                Assert.Contains("$ 175 con la versión 2", texto);
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        salida.WriteLine(ultimoViejo == TimeSpan.Zero
            ? "Al volver Valkey no se vio el precio viejo."
            : $"El precio viejo se vio hasta {ultimoViejo.TotalSeconds:0.0} s después de guardado el puntero.");
        Assert.True(ultimoViejo < TimeSpan.FromSeconds(61),
            $"El precio viejo seguía vigente {ultimoViejo.TotalSeconds:0.0} s después de guardado el puntero.");
        Assert.Contains("$ 175 con la versión 2", await CalcularAsync(admin, ZonaDemo, "Estandar"));
    }

    private async Task<bool> HayClavesAsync()
    {
        await using var conexion = await ConnectionMultiplexer.ConnectAsync(Conexion);
        await foreach (var _ in conexion.GetServers()[0].KeysAsync(pattern: "logistica:*"))
        {
            return true;
        }

        return false;
    }

    // Hace de OpenTelemetry: suma logistica.cache.errores, de cualquier caché. La colección corre sola, así que no se
    // mezcla con otras pruebas.
    private sealed class ContadorErroresCache : IDisposable
    {
        private readonly MeterListener oyente = new();
        private long total;

        public ContadorErroresCache()
        {
            oyente.InstrumentPublished = (instrumento, o) =>
            {
                if (instrumento.Meter.Name == MetricasCache.Medidor && instrumento.Name == "logistica.cache.errores")
                {
                    o.EnableMeasurementEvents(instrumento);
                }
            };
            oyente.SetMeasurementEventCallback<long>((_, valor, _, _) => Interlocked.Add(ref total, valor));
            oyente.Start();
        }

        public long Total => Interlocked.Read(ref total);

        public void Dispose() => oyente.Dispose();
    }
}
