namespace Logistica.Http.Contracts.Planificacion;

public sealed record PrevalidarRutaRequest(DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, IReadOnlyList<Guid> EnvioIds,
    Guid? RutaId = null, long? RevisionEsperada = null);
