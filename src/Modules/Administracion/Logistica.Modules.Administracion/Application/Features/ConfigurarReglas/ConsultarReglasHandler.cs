using Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;
using Logistica.Modules.Administracion.Domain;
using Logistica.Modules.Administracion.Domain.Reglas;

namespace Logistica.Modules.Administracion.Application.Features.ConfigurarReglas;

internal sealed record VersionReglasResumenDto(Guid Id, int Numero, SituacionVersion Situacion, DateTimeOffset? VigenteDesde,
    DateTimeOffset? VigenteHasta);

internal sealed record MotivoDto(Guid Id, string Codigo, string Descripcion, bool Reprogramable, bool ExigeEvidencia);

internal sealed record VersionReglasDetalleDto(Guid Id, int Numero, SituacionVersion Situacion, DateTimeOffset? VigenteDesde,
    DateTimeOffset? VigenteHasta, DatosReglas Valores, IReadOnlyList<MotivoDto> Motivos, IReadOnlyList<string> Errores);

// CU-06, pasos 1 y 2: las versiones de reglas del operador y el detalle de una. Sólo lee: el filtro "Tenant" deja ver
// únicamente las del operador de la sesión. La situación (vigente, programada…) se decide como en el tarifario.
internal sealed class ConsultarReglasHandler(IReglasRepository versiones, TimeProvider reloj)
{
    public async Task<IReadOnlyList<VersionReglasResumenDto>> ListarAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        return (await versiones.ListarAsync(ct))
            .Select(v => new VersionReglasResumenDto(v.Id, v.Numero, Situacion(v, ahora), v.VigenteDesde, v.VigenteHasta))
            .ToList();
    }

    public async Task<VersionReglasDetalleDto?> ObtenerBorradorAsync(CancellationToken ct)
    {
        return await versiones.ObtenerBorradorAsync(ct) is { } borrador ? Detalle(borrador) : null;
    }

    // Una versión de otro operador no se encuentra: null, y la página responde 404 (ADR-0002, sección 2.9).
    public async Task<VersionReglasDetalleDto?> ObtenerAsync(int numero, CancellationToken ct)
    {
        return await versiones.ObtenerPorNumeroAsync(numero, ct) is { } version ? Detalle(version) : null;
    }

    // Los errores se calculan siempre, así el borrador muestra qué falta antes de publicar.
    private VersionReglasDetalleDto Detalle(VersionReglas version) => new(version.Id, version.Numero,
        Situacion(version, reloj.GetUtcNow()), version.VigenteDesde, version.VigenteHasta, version.Valores(),
        version.Motivos.OrderBy(m => m.Codigo)
            .Select(m => new MotivoDto(m.Id, m.Codigo, m.Descripcion, m.Reprogramable, m.ExigeEvidencia)).ToList(),
        version.Estado == EstadoVersion.Borrador ? version.Errores() : []);

    private static SituacionVersion Situacion(VersionReglas version, DateTimeOffset ahora) => version switch
    {
        { Estado: EstadoVersion.Borrador } => SituacionVersion.Borrador,
        _ when version.RigeEn(ahora) => SituacionVersion.Vigente,
        { VigenteDesde: DateTimeOffset desde } when desde > ahora => SituacionVersion.Programada,
        _ => SituacionVersion.Reemplazada,
    };
}
