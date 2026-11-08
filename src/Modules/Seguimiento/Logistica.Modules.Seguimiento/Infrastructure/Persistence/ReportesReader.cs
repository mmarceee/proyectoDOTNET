using Logistica.Modules.Seguimiento.Application.Features.ConsultarReportes;
using Logistica.Modules.Seguimiento.Domain.Indicadores;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

// CU-72: agrega en PostgreSQL la tabla de lectura de los indicadores (CU-71). El filtro "Tenant" deja sólo las filas
// del operador de la sesión. Sin seguimiento de cambios: no modifica datos.
internal sealed class ReportesReader(SeguimientoDbContext db) : IReportesReader
{
    public async Task<IReadOnlyList<CumplimientoAgrupado>> CumplimientoAsync(DimensionCumplimiento dimension,
        DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var registros = Periodo(desde, hasta).Where(r => r.Tipo != TipoRegistroCumplimiento.Alta);
        var grupos = dimension switch
        {
            DimensionCumplimiento.Repartidor => registros.GroupBy(r => r.RepartidorId),
            DimensionCumplimiento.Comercio => registros.GroupBy(r => (Guid?)r.ComercioId),
            _ => registros.GroupBy(r => r.ZonaId),
        };
        return await grupos
            .Select(g => new CumplimientoAgrupado(g.Key,
                g.Sum(r => r.Tipo == TipoRegistroCumplimiento.Entrega ? 1 : 0),
                g.Sum(r => r.Tipo == TipoRegistroCumplimiento.Entrega && r.ATiempo != null ? 1 : 0),
                g.Sum(r => r.ATiempo == true ? 1 : 0),
                g.Sum(r => r.Tipo == TipoRegistroCumplimiento.IntentoFallido ? 1 : 0)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MotivoAgrupado>> MotivosAsync(DateTimeOffset desde, DateTimeOffset hasta,
        CancellationToken ct)
    {
        return await Periodo(desde, hasta)
            .Where(r => r.Tipo == TipoRegistroCumplimiento.IntentoFallido && r.Motivo != null)
            .GroupBy(r => r.Motivo!)
            .Select(g => new MotivoAgrupado(g.Key, g.Count()))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RegistroLeido>> RegistrosAsync(DateTimeOffset desde, DateTimeOffset hasta,
        CancellationToken ct)
    {
        return await Periodo(desde, hasta).Select(r => new RegistroLeido(r.Tipo, r.OcurridoEn)).ToListAsync(ct);
    }

    private IQueryable<RegistroCumplimiento> Periodo(DateTimeOffset desde, DateTimeOffset hasta) =>
        db.RegistrosCumplimiento.AsNoTracking().Where(r => r.OcurridoEn >= desde && r.OcurridoEn < hasta);
}
