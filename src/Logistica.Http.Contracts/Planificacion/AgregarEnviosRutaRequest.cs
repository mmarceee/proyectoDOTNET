namespace Logistica.Http.Contracts.Planificacion;

public sealed record AgregarEnviosRutaRequest(long RevisionEsperada, IReadOnlyList<Guid> EnvioIds);
