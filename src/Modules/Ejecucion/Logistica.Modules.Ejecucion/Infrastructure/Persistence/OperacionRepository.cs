using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence;

// Las consultas pasan por el filtro global "Tenant" (ADR-0002): sólo ven las operaciones del operador de la sesión.
internal sealed class OperacionRepository(EjecucionDbContext db) : IOperacionRepository
{
    public Task<OperacionDispositivo?> ObtenerAsync(Guid operacionId, CancellationToken ct)
    {
        return db.Operaciones.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operacionId, ct);
    }

    public void Agregar(OperacionDispositivo operacion)
    {
        db.Operaciones.Add(operacion);
    }

    public Task GuardarCambiosAsync(CancellationToken ct)
    {
        return db.SaveChangesAsync(ct);
    }
}
