namespace Logistica.Modules.Planificacion.Application.Features.AnularDespacho;

internal sealed record AnularDespachoCommand(Guid RutaId, long RevisionEsperada, string Motivo,
    bool ConfirmoSinCargaPendiente);
