using Logistica.Modules.Ejecucion.Contracts.Results;

namespace Logistica.Modules.Ejecucion.Contracts;

// CU-55 consulta al dueño de Rendicion. El operador siempre proviene de la sesión.
public interface IEjecucionModuleApi
{
    Task<RendicionPendiente?> ConsultarRendicionAsync(Guid rutaId, CancellationToken ct);
    Task<IReadOnlyList<RendicionPendiente>> ListarRendicionesPendientesAsync(int pagina, CancellationToken ct);
}
