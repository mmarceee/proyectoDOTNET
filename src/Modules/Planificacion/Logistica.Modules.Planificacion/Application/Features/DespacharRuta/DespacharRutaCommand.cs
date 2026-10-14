namespace Logistica.Modules.Planificacion.Application.Features.DespacharRuta;

internal sealed record DespacharRutaCommand(Guid RutaId, long RevisionEsperada);
