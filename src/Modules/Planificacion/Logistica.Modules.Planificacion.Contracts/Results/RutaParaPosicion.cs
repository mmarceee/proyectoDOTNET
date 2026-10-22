namespace Logistica.Modules.Planificacion.Contracts.Results;

public sealed record RutaParaPosicion(Guid VehiculoId, string Estado, DateTimeOffset? DespachadaEn);
