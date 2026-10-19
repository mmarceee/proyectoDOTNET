using Logistica.Modules.Administracion.Domain.Planificacion;

namespace Logistica.Modules.Administracion.Application.Features.DefinirZonas;

internal sealed record ZonaResumenDto(Guid Id, string Codigo, string Nombre, int CantidadCodigosPostales, bool Activa);

internal sealed record FranjaDto(Guid Id, TimeOnly Desde, TimeOnly Hasta, IReadOnlyList<DayOfWeek> Dias,
    DateOnly VigenteDesde, DateOnly? VigenteHasta);

internal sealed record ZonaDetalleDto(Guid Id, string Codigo, string Nombre, IReadOnlyList<string> CodigosPostales,
    bool Activa, IReadOnlyList<FranjaDto> Franjas);

// CU-04, paso 2: el listado de zonas y el detalle de una, con sus franjas. Sólo lee: el filtro "Tenant" deja ver
// únicamente las zonas del operador de la sesión.
internal sealed class ConsultarZonasHandler(IZonaRepository zonas)
{
    public async Task<IReadOnlyList<ZonaResumenDto>> ListarAsync(CancellationToken ct)
    {
        var todas = await zonas.ListarAsync(ct);
        return todas.OrderBy(z => z.Codigo)
            .Select(z => new ZonaResumenDto(z.Id, z.Codigo, z.Nombre, z.CodigosPostales.Length, z.Activa))
            .ToList();
    }

    // Una zona de otro operador no se encuentra: null, y la página responde 404 (ADR-0002, sección 2.9).
    public async Task<ZonaDetalleDto?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        if (await zonas.ObtenerAsync(id, ct) is not Zona zona)
        {
            return null;
        }

        var franjas = await zonas.FranjasDeAsync(id, ct);
        return new ZonaDetalleDto(zona.Id, zona.Codigo, zona.Nombre, zona.CodigosPostales, zona.Activa,
            franjas.OrderBy(f => f.VigenteDesde).ThenBy(f => f.HoraDesde)
                .Select(f => new FranjaDto(f.Id, f.HoraDesde, f.HoraHasta, f.Dias.Select(d => (DayOfWeek)d).ToList(),
                    f.VigenteDesde, f.VigenteHasta))
                .ToList());
    }
}
