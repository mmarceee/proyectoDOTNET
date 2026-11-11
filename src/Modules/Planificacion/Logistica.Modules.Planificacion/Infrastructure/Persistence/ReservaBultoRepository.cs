using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence;

internal sealed class ReservaBultoRepository(PlanificacionDbContext db) : IReservaBultoRepository
{
    public async Task<IReadOnlyList<ReservaBulto>> DeRutaAsync(Guid rutaId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.ReservasBulto.Where(r => r.RutaId == rutaId).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<Guid>> OcupadosAsync(IReadOnlyCollection<Guid> ids, Guid? propia, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.ReservasBulto.Where(r => ids.Contains(r.BultoId) && r.CerradaEn == null && r.RutaId != propia)
            .Select(r => r.BultoId).ToListAsync(ct);
    }
    public void Agregar(ReservaBulto reserva) => db.ReservasBulto.Add(reserva);
    public async Task<IReadOnlyList<ReservaBulto>> DelEnvioAsync(Guid envioId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.ReservasBulto.Where(r => r.EnvioId == envioId && r.CerradaEn == null).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<ReservaBulto>> ActivasAsync(IReadOnlyCollection<Guid> bultos, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.ReservasBulto.Where(r => bultos.Contains(r.BultoId) && r.CerradaEn == null).ToListAsync(ct);
    }
    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
