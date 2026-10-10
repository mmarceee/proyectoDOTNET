namespace Logistica.Http.Contracts.Planificacion;

public sealed record BultoValidadoResponse(Guid EnvioId, Guid BultoId, decimal PesoKg, decimal LargoCm,
    decimal AnchoCm, decimal AltoCm, decimal VolumenM3);
