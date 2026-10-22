using Logistica.SharedKernel;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// ADR-0002 §2.4: un operador por ámbito, fijado antes de resolver los DbContext.
// Sólo mantenimiento interno: no se obtiene de parámetros HTTP.
public sealed class InquilinoTrabajo : ICurrentTenant
{
    public Guid? OperadorId { get; private set; }
    public Guid? ComercioId => null;
    public void Fijar(Guid operadorId)
    {
        if (operadorId == Guid.Empty || OperadorId is not null)
            throw new InvalidOperationException("El trabajo necesita un operador válido y un ámbito nuevo.");
        OperadorId = operadorId;
    }
}
