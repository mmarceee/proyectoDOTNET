namespace Logistica.Http.Contracts.Planificacion;

public sealed record CrearRutaRequest(DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, IReadOnlyList<Guid> EnvioIds,
    IReadOnlyList<Guid>? BultoIds = null, Guid? OperacionId = null,
    IReadOnlyDictionary<Guid, long>? RevisionesBultos = null);
