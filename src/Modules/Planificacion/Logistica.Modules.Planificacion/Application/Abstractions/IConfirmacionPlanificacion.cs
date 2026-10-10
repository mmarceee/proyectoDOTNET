using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Abstractions;

internal interface IConfirmacionPlanificacion
{
    Task EjecutarAsync(Func<Task> trabajo, IReadOnlyCollection<Guid> envioIds, CancellationToken ct);
}
