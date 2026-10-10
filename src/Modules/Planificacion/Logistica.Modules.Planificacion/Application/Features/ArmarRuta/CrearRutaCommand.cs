namespace Logistica.Modules.Planificacion.Application.Features.ArmarRuta;

internal sealed record CrearRutaCommand(DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, IReadOnlyList<Guid> EnvioIds);
