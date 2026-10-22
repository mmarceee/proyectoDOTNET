namespace Logistica.Modules.Ejecucion.Contracts.Results;

// CU-66: ordenar por CapturadaEn y luego por Id. RecibidaEn no determina cuál es la última posición.
public sealed record PosicionVehiculoResult(Guid Id, Guid OperadorId, Guid VehiculoId, Guid RutaId,
    decimal Latitud, decimal Longitud, DateTimeOffset CapturadaEn, DateTimeOffset RecibidaEn);
