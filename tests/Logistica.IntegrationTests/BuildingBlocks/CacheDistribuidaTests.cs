using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Logistica.BuildingBlocks.Infrastructure.Caching;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.SharedKernel;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Logistica.IntegrationTests.BuildingBlocks;

public sealed class ValkeyFixture : IAsyncLifetime
{
    private readonly IContainer servidor = new ContainerBuilder("valkey/valkey:8.1")
        .WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    public string ConnectionString => $"{servidor.Hostname}:{servidor.GetMappedPublicPort(6379)}";
    public Task InitializeAsync() => servidor.StartAsync();
    public Task DisposeAsync() => servidor.DisposeAsync().AsTask();
}

// La caché distribuida (letra: "caché en funcionamiento con su métrica de aciertos"): aciertos y fallos, aislamiento
// por operador, invalidación, y que una caché caída no corte el request.
public sealed class CacheDistribuidaTests(ValkeyFixture valkey) : IClassFixture<ValkeyFixture>
{
    private static readonly Guid OperadorA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OperadorB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly TimeSpan Duracion = TimeSpan.FromMinutes(5);

    // Cada prueba usa su propio nombre de caché, para que sus métricas no se mezclen con las de otras en paralelo.
    private readonly string nombre = $"prueba-{Guid.NewGuid():N}";

    [Fact]
    public async Task La_primera_lectura_es_un_fallo_y_la_segunda_un_acierto_sin_volver_a_la_fuente()
    {
        using var metricas = new Metricas(nombre);
        await using var proveedor = Proveedor(OperadorA);
        var cache = proveedor.GetRequiredService<ICacheDistribuida>();
        var fuente = new Fuente();

        var primera = await cache.ObtenerOCrearAsync(nombre, "envio-1", fuente.LeerAsync, Duracion, default);
        var segunda = await cache.ObtenerOCrearAsync(nombre, "envio-1", fuente.LeerAsync, Duracion, default);

        Assert.Equal(primera, segunda);
        Assert.Equal(1, fuente.Lecturas);
        Assert.Equal((1L, 1L, 0L), (metricas.Aciertos, metricas.Fallos, metricas.Errores));
    }

    [Fact]
    public async Task Un_operador_no_lee_lo_que_guardo_otro_con_la_misma_clave()
    {
        var compartida = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        await using var proveedorA = Proveedor(OperadorA, compartida: compartida);
        await using var proveedorB = Proveedor(OperadorB, compartida: compartida);
        var fuente = new Fuente();

        var deA = await proveedorA.GetRequiredService<ICacheDistribuida>()
            .ObtenerOCrearAsync(nombre, "tarifario", fuente.LeerAsync, Duracion, default);
        var deB = await proveedorB.GetRequiredService<ICacheDistribuida>()
            .ObtenerOCrearAsync(nombre, "tarifario", fuente.LeerAsync, Duracion, default);

        Assert.NotEqual(deA, deB);
        Assert.Equal(2, fuente.Lecturas);
    }

    [Fact]
    public async Task Sin_operador_en_la_sesion_falla_cerrado()
    {
        await using var proveedor = Proveedor(operador: null);
        var cache = proveedor.GetRequiredService<ICacheDistribuida>();
        var fuente = new Fuente();

        await Assert.ThrowsAsync<TenantMismatchException>(() =>
            cache.ObtenerOCrearAsync(nombre, "envio-1", fuente.LeerAsync, Duracion, default));
        Assert.Equal(0, fuente.Lecturas);
    }

    [Fact]
    public async Task Invalidar_obliga_a_volver_a_la_fuente()
    {
        using var metricas = new Metricas(nombre);
        await using var proveedor = Proveedor(OperadorA);
        var cache = proveedor.GetRequiredService<ICacheDistribuida>();
        var fuente = new Fuente();

        var antes = await cache.ObtenerOCrearAsync(nombre, "tarifario", fuente.LeerAsync, Duracion, default);
        await cache.InvalidarAsync(nombre, "tarifario", default);
        var despues = await cache.ObtenerOCrearAsync(nombre, "tarifario", fuente.LeerAsync, Duracion, default);

        Assert.NotEqual(antes, despues);
        Assert.Equal(2, fuente.Lecturas);
        Assert.Equal((0L, 2L), (metricas.Aciertos, metricas.Fallos));
    }

    [Fact]
    public async Task Un_null_no_se_guarda()
    {
        await using var proveedor = Proveedor(OperadorA);
        var cache = proveedor.GetRequiredService<ICacheDistribuida>();
        var lecturas = 0;
        Task<Dato?> Inexistente(CancellationToken _)
        {
            lecturas++;
            return Task.FromResult<Dato?>(null);
        }

        await cache.ObtenerOCrearAsync(nombre, "envio-inexistente", Inexistente, Duracion, default);
        await cache.ObtenerOCrearAsync(nombre, "envio-inexistente", Inexistente, Duracion, default);

        Assert.Equal(2, lecturas);
    }

    [Fact]
    public async Task Si_la_cache_falla_la_lectura_sigue_desde_la_fuente_y_cuenta_un_error()
    {
        using var metricas = new Metricas(nombre);
        await using var proveedor = Proveedor(OperadorA, compartida: new CacheCaida());
        var cache = proveedor.GetRequiredService<ICacheDistribuida>();
        var fuente = new Fuente();

        var valor = await cache.ObtenerOCrearAsync(nombre, "envio-1", fuente.LeerAsync, Duracion, default);
        await cache.InvalidarAsync(nombre, "envio-1", default);

        Assert.Equal(new Dato("valor 1"), valor);
        Assert.Equal((0L, 0L, 2L), (metricas.Aciertos, metricas.Fallos, metricas.Errores));
    }

    [Fact]
    public async Task Con_Valkey_una_instancia_lee_lo_que_guardo_otra_y_la_invalidacion_llega_a_las_dos()
    {
        using var metricas = new Metricas(nombre);
        await using var api1 = Proveedor(OperadorA, valkey.ConnectionString);
        await using var api2 = Proveedor(OperadorA, valkey.ConnectionString);
        var fuente = new Fuente();

        var guardado = await api1.GetRequiredService<ICacheDistribuida>()
            .ObtenerOCrearAsync(nombre, "envio-1", fuente.LeerAsync, Duracion, default);
        var leido = await api2.GetRequiredService<ICacheDistribuida>()
            .ObtenerOCrearAsync(nombre, "envio-1", fuente.LeerAsync, Duracion, default);
        await api2.GetRequiredService<ICacheDistribuida>().InvalidarAsync(nombre, "envio-1", default);
        await api1.GetRequiredService<ICacheDistribuida>()
            .ObtenerOCrearAsync(nombre, "envio-1", fuente.LeerAsync, Duracion, default);

        Assert.Equal(guardado, leido);
        Assert.Equal(2, fuente.Lecturas);
        Assert.Equal((1L, 2L, 0L), (metricas.Aciertos, metricas.Fallos, metricas.Errores));
    }

    [Fact]
    public async Task Con_Valkey_inalcanzable_la_lectura_sigue_desde_la_fuente_sin_esperar()
    {
        using var metricas = new Metricas(nombre);
        // Nadie escucha en el puerto 1: es Valkey caído desde que arranca la API.
        await using var proveedor = Proveedor(OperadorA, "127.0.0.1:1");
        var cache = proveedor.GetRequiredService<ICacheDistribuida>();
        var fuente = new Fuente();

        var reloj = Stopwatch.StartNew();
        var valor = await cache.ObtenerOCrearAsync(nombre, "envio-1", fuente.LeerAsync, Duracion, default);
        reloj.Stop();

        Assert.Equal(new Dato("valor 1"), valor);
        Assert.Equal((0L, 0L, 1L), (metricas.Aciertos, metricas.Fallos, metricas.Errores));
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(5), $"Tardó {reloj.Elapsed}.");
    }

    private static ServiceProvider Proveedor(Guid? operador, string? conexion = null, IDistributedCache? compartida = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddScoped<ICurrentTenant>(_ => new InquilinoFijo(operador, null));
        services.AddCacheDistribuida(conexion);
        if (compartida is not null)
        {
            services.AddSingleton(compartida);
        }

        return services.BuildServiceProvider();
    }

    internal sealed record Dato(string Texto);

    // La base de datos de la prueba: cada lectura devuelve un valor distinto, así se nota si vino de la caché.
    private sealed class Fuente
    {
        public int Lecturas { get; private set; }

        public Task<Dato> LeerAsync(CancellationToken ct)
        {
            Lecturas++;
            return Task.FromResult(new Dato($"valor {Lecturas}"));
        }
    }

    // Hace de OpenTelemetry: escucha el medidor de la caché y suma lo de la caché de esta prueba.
    private sealed class Metricas : IDisposable
    {
        private readonly MeterListener oyente = new();
        private readonly ConcurrentDictionary<string, long> totales = new();

        public Metricas(string cache)
        {
            oyente.InstrumentPublished = (instrumento, o) =>
            {
                if (instrumento.Meter.Name == MetricasCache.Medidor)
                {
                    o.EnableMeasurementEvents(instrumento);
                }
            };
            oyente.SetMeasurementEventCallback<long>((instrumento, valor, etiquetas, _) =>
            {
                foreach (var etiqueta in etiquetas)
                {
                    if (etiqueta.Key == "cache" && Equals(etiqueta.Value, cache))
                    {
                        totales.AddOrUpdate(instrumento.Name, valor, (_, total) => total + valor);
                    }
                }
            });
            oyente.Start();
        }

        public long Aciertos => totales.GetValueOrDefault("logistica.cache.aciertos");
        public long Fallos => totales.GetValueOrDefault("logistica.cache.fallos");
        public long Errores => totales.GetValueOrDefault("logistica.cache.errores");

        public void Dispose() => oyente.Dispose();
    }

    // Valkey caído a mitad de camino: toda operación falla.
    private sealed class CacheCaida : IDistributedCache
    {
        private static Exception Caida() => new InvalidOperationException("Valkey no responde.");
        public byte[]? Get(string key) => throw Caida();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Caida();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Caida();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => throw Caida();
        public void Refresh(string key) => throw Caida();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Caida();
        public void Remove(string key) => throw Caida();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Caida();
    }
}
