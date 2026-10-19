using Logistica.Modules.Administracion.Contracts.Results;

namespace Logistica.Modules.Administracion.Contracts;

public interface IAdministracionModuleApi
{
    Task BloquearPlanificacionAsync(CancellationToken ct);
    Task<ContextoPlanificacion> ObtenerContextoPlanificacionAsync(CancellationToken ct);
    Task<RecursosPlanificacion> ConsultarRecursosPlanificacionAsync(DateOnly fecha, CancellationToken ct);
    Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct);
    Task<IReadOnlyList<FranjaPlanificacion>> ResolverFranjasAsync(IReadOnlyCollection<Guid> ids, DateOnly fecha, CancellationToken ct);

    // CU-05 para CU-10 (paso 7): el precio de un bulto con la versión del cuadro tarifario que rige ahora, leída de la
    // caché (ADR-0006). null si no hay versión vigente o ninguna regla cubre el bulto (CU-10, A3).
    Task<TarifaBulto?> CalcularTarifaAsync(Guid zonaId, Modalidad modalidad, decimal pesoKg, decimal volumenM3,
        CancellationToken ct);
}
