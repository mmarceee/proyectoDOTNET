using Logistica.Modules.Deposito.Contracts;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence;

internal sealed class DepositoModuleApi(DepositoDbContext db, ICurrentTenant tenant) : IDepositoModuleApi
{
    public async Task<IReadOnlyList<MedidasBultoRecibido>> ObtenerMedidasRecepcionAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
    {
        if (tenant.OperadorId is not Guid operadorId || bultoIds.Count == 0) return [];
        return await db.Recepciones.AsNoTracking().Where(r => r.OperadorId == operadorId && bultoIds.Contains(r.BultoId))
            .Select(r => new MedidasBultoRecibido(r.BultoId, r.PesoKg, r.LargoCm, r.AnchoCm, r.AltoCm, r.RecibidoEn)).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<Guid>> BultosRecibidosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
    {
        if (tenant.OperadorId is not Guid operadorId || bultoIds.Count == 0)
        {
            return [];
        }

        return await db.Recepciones.AsNoTracking()
            .Where(r => r.OperadorId == operadorId && bultoIds.Contains(r.BultoId))
            .Select(r => r.BultoId)
            .ToListAsync(ct);
    }
}
