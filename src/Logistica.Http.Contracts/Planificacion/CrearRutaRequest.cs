namespace Logistica.Http.Contracts.Planificacion;

public sealed record CrearRutaRequest(DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, IReadOnlyList<Guid> EnvioIds);
