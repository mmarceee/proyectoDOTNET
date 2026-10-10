using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;

namespace Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;

internal interface IRecursosPlanificacionReader
{
    Task BloquearPlanificacionAsync(CancellationToken ct);
    Task<ContextoPlanificacion> ObtenerContextoPlanificacionAsync(CancellationToken ct);
    Task<DatosRecursosPlanificacion> LeerRecursosAsync(CancellationToken ct);
    Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct);
}
