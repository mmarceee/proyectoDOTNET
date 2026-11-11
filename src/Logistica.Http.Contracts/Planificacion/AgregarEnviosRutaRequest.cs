namespace Logistica.Http.Contracts.Planificacion;

public sealed record AgregarEnviosRutaRequest(long RevisionEsperada, IReadOnlyList<Guid> EnvioIds,
    IReadOnlyList<Guid>? BultoIds = null, Guid? OperacionId = null,
    IReadOnlyDictionary<Guid, long>? RevisionesBultos = null);
