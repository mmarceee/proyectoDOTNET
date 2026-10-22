using Logistica.Http.Contracts.Avisos;
using Logistica.Modules.Seguimiento.Application.Features.HistorialAvisos;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class HistorialAvisosReader(SeguimientoDbContext db, IConsultaConsistente lectura) : IHistorialAvisosReader
{
    private IQueryable<EntregaAvisoResumen> Proyectar(IQueryable<EntregaAviso> entregas) =>
        from e in entregas
        join s in db.SuscripcionesAviso.AsNoTracking() on e.SuscripcionAvisoId equals s.Id
        orderby e.FechaEvento descending, e.Id descending
        select new EntregaAvisoResumen(e.Id, e.FechaEvento, e.NumeroEnvio, e.EstadoEnvio, e.Tipo.ToString(),
            e.Estado.ToString(), e.Intentos.Count, s.Activa, s.Activa && e.Estado == EstadoEntregaAviso.EnColaDeFallidos);

    public Task<HistorialAvisosResponse> ConsultarAsync(HistorialAvisosQuery query, CancellationToken ct) =>
        lectura.LeerAsync(async () =>
        {
            await db.ParticiparEnTransaccionAsync(ct);
            var filas = db.EntregasAviso.AsNoTracking().AsQueryable();
            if (query.Estado is { } estado) filas = filas.Where(e => e.Estado == estado);
            if (query.Desde is { } desde) filas = filas.Where(e => e.FechaEvento >= desde);
            if (query.HastaExclusiva is { } hasta) filas = filas.Where(e => e.FechaEvento < hasta);
            var total = await filas.CountAsync(ct);
            var pagina = filas.OrderByDescending(e => e.FechaEvento).ThenByDescending(e => e.Id)
                .Skip((query.Pagina - 1) * query.TamanoPagina).Take(query.TamanoPagina);
            return new HistorialAvisosResponse(await Proyectar(pagina).ToListAsync(ct), query.Pagina, query.TamanoPagina, total);
        }, ct);

    public Task<DetalleEntregaAvisoResponse?> ObtenerAsync(Guid id, CancellationToken ct) =>
        lectura.LeerAsync<DetalleEntregaAvisoResponse?>(async () =>
        {
            await db.ParticiparEnTransaccionAsync(ct);
            var fila = db.EntregasAviso.AsNoTracking().Where(e => e.Id == id);
            var resumen = await Proyectar(fila).SingleOrDefaultAsync(ct);
            if (resumen is null) return null;
            var url = await (from e in fila join s in db.SuscripcionesAviso on e.SuscripcionAvisoId equals s.Id
                select s.UrlDestino).SingleAsync(ct);
            var intentos = await db.Set<IntentoAviso>().AsNoTracking().Where(i => i.EntregaAvisoId == id)
                .OrderBy(i => i.Numero).Select(i => new IntentoAvisoResponse(i.Numero, i.EnviadoEn, i.Exitoso,
                    i.CodigoRespuesta, i.Error)).ToListAsync(ct);
            return new(resumen, url, intentos);
        }, ct);
}
