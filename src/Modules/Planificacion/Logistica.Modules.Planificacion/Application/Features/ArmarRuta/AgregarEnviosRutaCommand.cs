namespace Logistica.Modules.Planificacion.Application.Features.ArmarRuta;

internal sealed record AgregarEnviosRutaCommand(long RevisionEsperada, IReadOnlyList<Guid> EnvioIds);
