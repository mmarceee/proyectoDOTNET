namespace Logistica.Http.Contracts.Planificacion;

public sealed record AnularDespachoRequest(long RevisionEsperada, string Motivo, bool ConfirmoSinCargaPendiente);
