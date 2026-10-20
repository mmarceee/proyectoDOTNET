namespace Logistica.Http.Contracts.Planificacion;

public sealed record AnulacionDespachoResponse(Guid Id, DateOnly FechaRuta, DateTimeOffset DespachadaEn,
    long RevisionAnterior, long RevisionRuta, string Motivo, Guid? ResponsableId, DateTimeOffset OcurridoEn);
