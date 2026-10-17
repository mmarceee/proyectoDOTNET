using Logistica.Modules.Planificacion.Contracts;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence;

internal sealed class HojaRutaPlanificacion(PlanificacionDbContext db) : IHojaRutaPlanificacion
{
    public async Task<RutaParaHoja?> ConsultarAsync(Guid repartidorId, DateOnly fecha, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        var rutas = await db.Rutas.AsNoTracking().Include(r => r.Paradas).ThenInclude(p => p.Envios)
            .Where(r => r.RepartidorId == repartidorId && r.Fecha == fecha
                && (r.Estado == EstadoRuta.Despachada || r.Estado == EstadoRuta.EnCurso))
            .Take(2).ToListAsync(ct);
        if (rutas.Count > 1) throw new InvalidOperationException("Hay más de una ruta activa para el repartidor.");
        var ruta = rutas.SingleOrDefault();
        return ruta is null ? null : new(ruta.Id, ruta.VehiculoId, ruta.Revision, ruta.Fecha, ruta.Estado.ToString(),
            ruta.Paradas.OrderBy(p => p.Orden).Select(p => new ParadaParaHoja(p.Id, p.Orden, p.Estado.ToString(),
                p.Direccion, p.Desde, p.Hasta, p.Envios.Select(e => e.EnvioId).OrderBy(id => id).ToArray())).ToArray());
    }
}
