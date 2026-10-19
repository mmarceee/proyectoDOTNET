using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.ConfigurarReglas;

// La lectura de las reglas operativas con la caché (ADR-0006, sección 2.4), igual que TarifarioVigente:
// - "vigente": qué número rige ahora. 10 minutos, se borra al publicar y no se usa más allá de la próxima programada.
// - "v{numero}" e "id:{versionId}": una versión publicada. 24 horas; nunca hace falta borrarlas, porque no cambian.
// Se guarda directamente el resultado del contrato (ReglasOperativas): es lo que piden los demás módulos.
internal sealed class ReglasVigentes(IReglasRepository versiones, ICacheDistribuida cache, TimeProvider reloj)
{
    public const string Cache = "reglas";
    private const string ClaveVigente = "vigente";
    private static readonly TimeSpan DuracionVigente = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DuracionVersion = TimeSpan.FromHours(24);

    internal sealed record Puntero(int? Numero, DateTimeOffset? ValidoHasta);

    public async Task<ReglasOperativas?> VigentesAsync(CancellationToken ct)
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
            async c => Resultado(await versiones.ObtenerPorNumeroAsync(numero, c)), DuracionVersion, ct);
    }

    // Las de una versión publicada, por su Id: las que guardó el envío al admitirse.
    public Task<ReglasOperativas?> DeVersionAsync(Guid versionId, CancellationToken ct)
    {
        return cache.ObtenerOCrearAsync(Cache, $"id:{versionId:N}",
            async c => Resultado(await versiones.ObtenerAsync(versionId, c)), DuracionVersion, ct);
    }

    // Se llama después de guardar la publicación (ADR-0006, sección 2.5).
    public Task InvalidarAsync(CancellationToken ct)
    {
        return cache.InvalidarAsync(Cache, ClaveVigente, ct);
    }

    private async Task<Puntero> CargarPunteroAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        var vigente = await versiones.VigenteEnAsync(ahora, ct);
        return new Puntero(vigente?.Numero, await versiones.ProximoInicioAsync(ahora, ct));
    }

    private static ReglasOperativas? Resultado(VersionReglas? version) => version is null ? null : new ReglasOperativas(
        version.Id, version.Numero,
        version.MaxIntentos, TimeSpan.FromHours(version.PlazoEntreIntentosHoras),
        version.PruebaExigida.ExigeFoto, version.PruebaExigida.ExigeNombreYDocumento,
        version.PoliticaDevolucion.DevolverTrasIntentosAgotados, version.PoliticaDevolucion.PlazoDevolucionDias,
        version.Plazos.Select(p => new PlazoModalidadResultado(p.Modalidad, TimeSpan.FromHours(p.PlazoHoras))).ToList(),
        TimeSpan.FromHours(version.AnticipacionReprogramacionHoras),
        version.MaxParadasPorRuta,
        version.Motivos.OrderBy(m => m.Codigo)
            .Select(m => new MotivoNoEntregaResultado(m.Id, m.Codigo, m.Descripcion, m.Reprogramable, m.ExigeEvidencia))
            .ToList());
}
