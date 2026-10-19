using Logistica.Modules.Administracion.Domain.Tarifas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;

// La lectura del cuadro tarifario que rige, con la caché (ADR-0006, sección 2.4). Se lee mucho (cada bulto de cada
// envío) y cambia poco (cuando el administrador publica):
// - "vigente": qué número rige ahora. Dura 10 minutos, se borra al publicar y nunca se usa más allá del inicio de la
//   próxima versión programada, porque ese día cambia sin que nadie publique nada.
// - "v{numero}": la versión publicada. Dura 24 horas y nunca hace falta borrarla: una versión publicada no cambia.
// La caché agrega el operador de la sesión a cada clave (ICacheDistribuida).
internal sealed class TarifarioVigente(ITarifarioRepository versiones, ICacheDistribuida cache, TimeProvider reloj)
{
    public const string Cache = "tarifario";
    private const string ClaveVigente = "vigente";
    private static readonly TimeSpan DuracionVigente = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DuracionVersion = TimeSpan.FromHours(24);

    // El número que rige y hasta cuándo se puede confiar en él (el inicio de la próxima programada, si hay).
    internal sealed record Puntero(int? Numero, DateTimeOffset? ValidoHasta);

    public async Task<TablaTarifaria?> ObtenerAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var puntero = await cache.ObtenerOCrearAsync(Cache, ClaveVigente, c => CargarPunteroAsync(ahora, c), DuracionVigente, ct);

        // Empezó a regir una versión programada: el puntero guardado quedó viejo antes de vencer.
        if (puntero.ValidoHasta is DateTimeOffset hasta && hasta <= ahora)
        {
            await cache.InvalidarAsync(Cache, ClaveVigente, ct);
            puntero = await cache.ObtenerOCrearAsync(Cache, ClaveVigente, c => CargarPunteroAsync(ahora, c), DuracionVigente, ct);
        }

        if (puntero.Numero is not int numero)
        {
            return null;
        }

        return await cache.ObtenerOCrearAsync(Cache, $"v{numero}",
            async c => (await versiones.ObtenerPorNumeroAsync(numero, c))?.Tabla(), DuracionVersion, ct);
    }

    // Se llama después de guardar la publicación (ADR-0006, sección 2.5): la próxima lectura vuelve a la base.
    public Task InvalidarAsync(CancellationToken ct)
    {
        return cache.InvalidarAsync(Cache, ClaveVigente, ct);
    }

    private async Task<Puntero> CargarPunteroAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        var vigente = await versiones.VigenteEnAsync(ahora, ct);
        return new Puntero(vigente?.Numero, await versiones.ProximoInicioAsync(ahora, ct));
    }
}
