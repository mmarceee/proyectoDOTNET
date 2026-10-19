using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain;
using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Administracion.Domain.Tarifas;

namespace Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;

// Cómo se muestra una versión: lo decide el momento, no sólo el estado (decisión D3).
internal enum SituacionVersion
{
    Borrador,
    Vigente,
    Programada,
    Reemplazada,
}

internal sealed record VersionResumenDto(Guid Id, int Numero, SituacionVersion Situacion, DateTimeOffset? VigenteDesde,
    DateTimeOffset? VigenteHasta);

internal sealed record ReglaDto(Guid Id, Guid ZonaId, string Zona, Modalidad Modalidad, decimal PesoDesdeKg,
    decimal PesoHastaKg, decimal VolumenDesdeM3, decimal VolumenHastaM3, decimal PrecioBase);

internal sealed record AjusteDto(Guid Id, TipoAjuste Tipo, bool EsPorcentaje, decimal Valor, Modalidad Modalidad);

internal sealed record VersionDetalleDto(Guid Id, int Numero, SituacionVersion Situacion, DateTimeOffset? VigenteDesde,
    DateTimeOffset? VigenteHasta, IReadOnlyList<ReglaDto> Reglas, IReadOnlyList<AjusteDto> Ajustes);

internal sealed record ZonaOpcionDto(Guid Id, string Codigo, string Nombre);

// CU-05, pasos 1 y 2: las versiones del operador (vigente, programada, anteriores y borrador) y el detalle de una. Sólo
// lee: el filtro "Tenant" deja ver únicamente las del operador de la sesión.
internal sealed class ConsultarTarifarioHandler(ITarifarioRepository versiones, IZonaRepository zonas, TimeProvider reloj)
{
    public async Task<IReadOnlyList<VersionResumenDto>> ListarAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        return (await versiones.ListarAsync(ct))
            .Select(v => new VersionResumenDto(v.Id, v.Numero, Situacion(v, ahora), v.VigenteDesde, v.VigenteHasta))
            .ToList();
    }

    public async Task<VersionDetalleDto?> ObtenerBorradorAsync(CancellationToken ct)
    {
        return await versiones.ObtenerBorradorAsync(ct) is { } borrador ? await DetalleAsync(borrador, ct) : null;
    }

    // Una versión de otro operador no se encuentra: null, y la página responde 404 (ADR-0002, sección 2.9).
    public async Task<VersionDetalleDto?> ObtenerAsync(int numero, CancellationToken ct)
    {
        return await versiones.ObtenerPorNumeroAsync(numero, ct) is { } version ? await DetalleAsync(version, ct) : null;
    }

    // Las zonas activas, para elegir la de una regla nueva.
    public async Task<IReadOnlyList<ZonaOpcionDto>> ZonasActivasAsync(CancellationToken ct)
    {
        return (await zonas.ListarAsync(ct)).Where(z => z.Activa).OrderBy(z => z.Codigo)
            .Select(z => new ZonaOpcionDto(z.Id, z.Codigo, z.Nombre)).ToList();
    }

    private async Task<VersionDetalleDto> DetalleAsync(VersionTarifario version, CancellationToken ct)
    {
        var nombres = (await zonas.ListarAsync(ct)).ToDictionary(z => z.Id, z => $"{z.Codigo} · {z.Nombre}");
        var reglas = version.Reglas
            .OrderBy(r => nombres.GetValueOrDefault(r.ZonaId)).ThenBy(r => r.Modalidad).ThenBy(r => r.PesoDesdeKg)
            .ThenBy(r => r.VolumenDesdeM3)
            .Select(r => new ReglaDto(r.Id, r.ZonaId, nombres.GetValueOrDefault(r.ZonaId) ?? "Zona eliminada", r.Modalidad,
                r.PesoDesdeKg, r.PesoHastaKg, r.VolumenDesdeM3, r.VolumenHastaM3, r.PrecioBase))
            .ToList();
        var ajustes = version.Ajustes.OrderBy(a => a.Modalidad).ThenBy(a => a.Tipo)
            .Select(a => new AjusteDto(a.Id, a.Tipo, a.EsPorcentaje, a.Valor, a.Modalidad)).ToList();

        return new VersionDetalleDto(version.Id, version.Numero, Situacion(version, reloj.GetUtcNow()),
            version.VigenteDesde, version.VigenteHasta, reglas, ajustes);
    }

    private static SituacionVersion Situacion(VersionTarifario version, DateTimeOffset ahora) => version switch
    {
        { Estado: EstadoVersion.Borrador } => SituacionVersion.Borrador,
        _ when version.RigeEn(ahora) => SituacionVersion.Vigente,
        { VigenteDesde: DateTimeOffset desde } when desde > ahora => SituacionVersion.Programada,
        _ => SituacionVersion.Reemplazada,
    };
}
