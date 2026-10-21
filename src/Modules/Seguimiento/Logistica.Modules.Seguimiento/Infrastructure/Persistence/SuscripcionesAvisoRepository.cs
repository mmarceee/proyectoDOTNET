using Logistica.Modules.Seguimiento.Domain.Avisos;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class SuscripcionesAvisoRepository(SeguimientoDbContext db) : ISuscripcionesAvisoRepository
{
    public void Agregar(SuscripcionAviso suscripcion) => db.SuscripcionesAviso.Add(suscripcion);

    public async Task<SuscripcionAviso?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.SuscripcionesAviso.SingleOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<SuscripcionAviso>> ListarAsync(CancellationToken ct) =>
        await db.SuscripcionesAviso.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<SuscripcionAviso>> ListarActivasAsync(Guid comercioId, CancellationToken ct) =>
        await db.SuscripcionesAviso.Where(s => s.ComercioId == comercioId && s.Activa).OrderBy(s => s.Id).ToListAsync(ct);

    public async Task GuardarAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
