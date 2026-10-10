using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Exceptions;

internal sealed class ConflictoRutaException(string codigo, string mensaje, IReadOnlyList<Guid>? envioIds = null,
    IReadOnlyList<string>? recursos = null, long? revision = null) : Exception(mensaje)
{
    public string Codigo { get; } = codigo;
    public IReadOnlyList<Guid> EnvioIds { get; } = envioIds ?? [];
    public IReadOnlyList<string> Recursos { get; } = recursos ?? [];
    public long? RevisionActual { get; } = revision;
}
