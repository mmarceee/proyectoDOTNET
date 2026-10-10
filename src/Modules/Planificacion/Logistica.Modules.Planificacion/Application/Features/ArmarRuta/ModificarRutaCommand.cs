namespace Logistica.Modules.Planificacion.Application.Features.ArmarRuta;

internal sealed record ModificarRutaCommand(long RevisionEsperada, DateOnly Fecha, Guid RepartidorId, Guid VehiculoId);
