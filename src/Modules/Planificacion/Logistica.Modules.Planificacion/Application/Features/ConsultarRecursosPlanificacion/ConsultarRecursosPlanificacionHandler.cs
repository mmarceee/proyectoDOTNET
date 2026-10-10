using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Planificacion.Application.Services;

namespace Logistica.Modules.Planificacion.Application.Features.ConsultarRecursosPlanificacion;

internal sealed class ConsultarRecursosPlanificacionHandler(IAdministracionModuleApi administracion, CalendarioPlanificacion calendario)
{
    public Task<RecursosPlanificacion> RecursosAsync(DateOnly fecha, CancellationToken ct)
        => administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
    public Task<DateOnly> HoyAsync(CancellationToken ct) => calendario.HoyAsync(ct);
}
