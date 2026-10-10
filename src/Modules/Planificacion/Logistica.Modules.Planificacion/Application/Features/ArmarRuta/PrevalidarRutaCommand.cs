namespace Logistica.Modules.Planificacion.Application.Features.ArmarRuta;

internal sealed record PrevalidarRutaCommand(DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, IReadOnlyList<Guid> EnvioIds, Guid? RutaId = null, long? RevisionEsperada = null);
