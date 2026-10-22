using Logistica.Modules.Ejecucion.Domain.Posiciones;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence;

internal sealed class PosicionRepository(EjecucionDbContext db) : IPosicionRepository
{
    // ADR-0002 §4: mantiene el filtro Tenant; no modifica identificadores de pertenencia.
    public Task<int> EliminarAnterioresAsync(DateTimeOffset desde, CancellationToken ct) =>
        db.Posiciones.Where(p => p.CapturadaEn < desde).ExecuteDeleteAsync(ct);
    public void Agregar(PosicionVehiculo posicion) => db.Posiciones.Add(posicion);

    public async Task<IReadOnlyList<PosicionVehiculo>> UltimasAsync(IReadOnlyCollection<Guid> vehiculos,
        DateTimeOffset desde, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Posiciones.AsNoTracking().Where(p => vehiculos.Contains(p.VehiculoId) && p.CapturadaEn >= desde)
            .GroupBy(p => p.VehiculoId).Select(g => g.OrderByDescending(p => p.CapturadaEn)
                .ThenByDescending(p => p.Id).First()).ToArrayAsync(ct);
    }
}
