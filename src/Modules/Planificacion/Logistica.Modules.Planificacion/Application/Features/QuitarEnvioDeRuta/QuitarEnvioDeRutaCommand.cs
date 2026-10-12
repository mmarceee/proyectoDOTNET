namespace Logistica.Modules.Planificacion.Application.Features.QuitarEnvioDeRuta;

internal sealed record QuitarEnvioDeRutaCommand(Guid RutaId, Guid EnvioId, long RevisionEsperada);
