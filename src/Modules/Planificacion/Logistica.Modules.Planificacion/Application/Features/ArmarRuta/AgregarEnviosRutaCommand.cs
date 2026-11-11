namespace Logistica.Modules.Planificacion.Application.Features.ArmarRuta;

internal sealed record AgregarEnviosRutaCommand(long RevisionEsperada, IReadOnlyList<Guid> EnvioIds,
    IReadOnlyList<Guid>? BultoIds = null, Guid? OperacionId = null,
    IReadOnlyDictionary<Guid, long>? RevisionesBultos = null);
