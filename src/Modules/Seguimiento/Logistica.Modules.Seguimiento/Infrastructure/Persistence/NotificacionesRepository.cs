using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class NotificacionesRepository(SeguimientoDbContext db) : INotificacionesRepository
{
    public void Agregar(Notificacion notificacion) => db.Notificaciones.Add(notificacion);

    public Task<Notificacion?> ObtenerPorEventoAsync(Guid eventoId, CancellationToken ct) =>
        db.Notificaciones.SingleOrDefaultAsync(n => n.EventoId == eventoId, ct);

    public async Task GuardarAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}

internal sealed class IdentidadesOperadorRepository(SeguimientoDbContext db) : IIdentidadesOperadorRepository
{
    public void Agregar(IdentidadOperador identidad) => db.IdentidadesOperador.Add(identidad);

    public Task<IdentidadOperador?> ObtenerAsync(Guid operadorId, CancellationToken ct) =>
        db.IdentidadesOperador.SingleOrDefaultAsync(i => i.OperadorId == operadorId, ct);

    public async Task GuardarAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
