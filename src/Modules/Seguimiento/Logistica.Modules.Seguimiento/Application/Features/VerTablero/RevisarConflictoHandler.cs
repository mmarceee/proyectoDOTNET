using Logistica.Modules.Ejecucion.Contracts;

namespace Logistica.Modules.Seguimiento.Application.Features.VerTablero;

// CU-66 (ADR-0004, sección 2.5): el despachador marca como revisado un conflicto de sincronización. El dueño de la
// operación y su evidencia es Ejecución: el tablero sólo se lo pide. La revisión no cambia el envío.
internal sealed class RevisarConflictoHandler(IEjecucionModuleApi ejecucion)
{
    // false: no existe o es de otro operador (404 genérico, ADR-0002, sección 2.9).
    public Task<bool> HandleAsync(Guid operacionId, CancellationToken ct)
    {
        return ejecucion.MarcarConflictoRevisadoAsync(operacionId, ct);
    }
}
