using Logistica.Modules.Administracion.Contracts.Results;

namespace Logistica.Modules.Administracion.Contracts;

public interface IAdministracionModuleApi
{
    Task BloquearPlanificacionAsync(CancellationToken ct);
    Task<ContextoPlanificacion> ObtenerContextoPlanificacionAsync(CancellationToken ct);
    Task<RecursosPlanificacion> ConsultarRecursosPlanificacionAsync(DateOnly fecha, CancellationToken ct);
    Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct);
    Task<IReadOnlyList<FranjaPlanificacion>> ResolverFranjasAsync(IReadOnlyCollection<Guid> ids, DateOnly fecha, CancellationToken ct);
}
