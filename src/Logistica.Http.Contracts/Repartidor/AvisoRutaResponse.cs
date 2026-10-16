namespace Logistica.Http.Contracts.Repartidor;

public sealed record AvisoRutaResponse(Guid Id, Guid RutaId, DateOnly FechaRuta, DateTimeOffset DespachadaEn, long Revision);
