using Logistica.Modules.Deposito.Domain.Recepciones;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence;

internal sealed class RecepcionRepository(DepositoDbContext db) : IRecepcionRepository
{
    public async Task<IReadOnlyList<Guid>> BultosRecibidosAsync(Guid envioId, CancellationToken ct)
    {
        // El filtro global "Tenant" deja sólo las recepciones del operador de la sesión (ADR-0002).
        return await db.Recepciones
            .Where(r => r.EnvioId == envioId)
            .Select(r => r.BultoId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RecepcionDeposito>> ConsultarPorBultosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
    {
        if (bultoIds.Count == 0) return [];
        return await db.Recepciones.AsNoTracking().Where(r => bultoIds.Contains(r.BultoId)).ToListAsync(ct);
    }

    public void Agregar(RecepcionDeposito recepcion)
    {
        db.Recepciones.Add(recepcion);
    }

    public Task GuardarCambiosAsync(CancellationToken ct)
    {
        return db.SaveChangesAsync(ct);
    }
}
