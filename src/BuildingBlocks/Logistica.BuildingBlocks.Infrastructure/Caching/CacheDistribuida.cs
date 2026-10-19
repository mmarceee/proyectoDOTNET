using System.Text.Json;
using Logistica.SharedKernel;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Logistica.BuildingBlocks.Infrastructure.Caching;

// ICacheDistribuida sobre IDistributedCache: Valkey en Docker Compose y producción, memoria del proceso para depurar
// sin Valkey y en las pruebas.
// - La clave lleva el operador de la sesión; sin operador falla cerrado, igual que AgregarAlOutbox (ADR-0002).
// - Si Valkey no responde, la lectura va a la fuente y el request sigue: la caché acelera, pero no es imprescindible.
// - Cada lectura suma un acierto o un fallo, y cada problema con Valkey un error, en MetricasCache.
internal sealed class CacheDistribuida(IDistributedCache cache, ICurrentTenant tenant, ILogger<CacheDistribuida> logger)
    : ICacheDistribuida
{
    public async Task<T> ObtenerOCrearAsync<T>(string nombre, string clave, Func<CancellationToken, Task<T>> crear,
        TimeSpan duracion, CancellationToken ct)
    {
        var claveCompleta = Clave(nombre, clave);
        var disponible = true;

        try
        {
            if (await cache.GetAsync(claveCompleta, ct) is byte[] guardado)
            {
                var valor = JsonSerializer.Deserialize<T>(guardado)!;
                MetricasCache.Acierto(nombre);
                return valor;
            }

            MetricasCache.Fallo(nombre);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Valkey caído o lento, o un valor guardado con otra forma: se lee de la fuente y no se intenta guardar.
            disponible = false;
            MetricasCache.Error(nombre);
            logger.LogWarning(e, "No se pudo leer {Clave} de la caché; se lee de la fuente.", claveCompleta);
        }

        var calculado = await crear(ct);
        if (disponible && calculado is not null)
        {
            await GuardarAsync(nombre, claveCompleta, calculado, duracion, ct);
        }

        return calculado;
    }

    public async Task InvalidarAsync(string nombre, string clave, CancellationToken ct)
    {
        var claveCompleta = Clave(nombre, clave);
        try
        {
            await cache.RemoveAsync(claveCompleta, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // El cambio ya se guardó en la base: no se deshace. El dato viejo vence con su duración.
            MetricasCache.Error(nombre);
            logger.LogWarning(e, "No se pudo invalidar {Clave} en la caché; vence con su duración.", claveCompleta);
        }
    }

    private async Task GuardarAsync<T>(string nombre, string claveCompleta, T valor, TimeSpan duracion,
        CancellationToken ct)
    {
        try
        {
            await cache.SetAsync(claveCompleta, JsonSerializer.SerializeToUtf8Bytes(valor),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = duracion }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            MetricasCache.Error(nombre);
            logger.LogWarning(e, "No se pudo guardar {Clave} en la caché.", claveCompleta);
        }
    }

    private string Clave(string nombre, string clave)
    {
        var operadorId = tenant.OperadorId
            ?? throw new TenantMismatchException($"Se intentó usar la caché {nombre} sin un operador en la sesión.");

        return $"{nombre}:{operadorId:N}:{clave}";
    }
}
