using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Rutas;

internal interface IConfirmacionPlanificacion
{
    Task EjecutarAsync(Func<Task> trabajo, IReadOnlyCollection<Guid> envioIds, CancellationToken ct);
}
internal interface IResponsablePlanificacion { Guid? Id { get; } }
internal sealed class ConflictoRutaException(string codigo, string mensaje, IReadOnlyList<Guid>? envioIds = null,
    IReadOnlyList<string>? recursos = null, long? revision = null) : Exception(mensaje)
{
    public string Codigo { get; } = codigo;
    public IReadOnlyList<Guid> EnvioIds { get; } = envioIds ?? [];
    public IReadOnlyList<string> Recursos { get; } = recursos ?? [];
    public long? RevisionActual { get; } = revision;
}
internal sealed class RutaNoEncontradaException : Exception;
internal sealed class RestriccionesRutaException(IReadOnlyList<Domain.Rutas.RestriccionRuta> restricciones)
    : DomainException(string.Join(" ", restricciones.Select(r => r.Mensaje)))
{
    public IReadOnlyList<Domain.Rutas.RestriccionRuta> Restricciones { get; } = restricciones;
}
