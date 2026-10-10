namespace Logistica.Http.Contracts.Planificacion;

public sealed record ModificarRutaRequest(long RevisionEsperada, DateOnly Fecha, Guid RepartidorId, Guid VehiculoId);
