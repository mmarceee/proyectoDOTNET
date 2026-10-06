using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence;

internal sealed class RecepcionRepository(DepositoDbContext db, ICurrentTenant tenant) : IRecepcionRepository
{
    public async Task<IReadOnlyList<Guid>> BultosRecibidosAsync(Guid envioId, CancellationToken ct)
    {
        // Filtro de inquilino manual hasta el filtro global "Tenant" (ADR-0002, 15/10). Falla cerrado.
        if (tenant.OperadorId is not Guid operadorId)
        {
            return [];
        }

        return await db.Recepciones
            .Where(r => r.OperadorId == operadorId && r.EnvioId == envioId)
            .Select(r => r.BultoId)
            .ToListAsync(ct);
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
