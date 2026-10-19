namespace Logistica.Modules.Planificacion.Contracts.Results;

public sealed record RutaParaCarga(long Revision, string Estado, IReadOnlyList<Guid> Envios);
