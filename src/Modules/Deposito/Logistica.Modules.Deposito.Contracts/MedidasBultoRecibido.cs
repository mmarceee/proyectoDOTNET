namespace Logistica.Modules.Deposito.Contracts;

public sealed record MedidasBultoRecibido(Guid BultoId, decimal? PesoKg, decimal? LargoCm, decimal? AnchoCm,
    decimal? AltoCm, DateTimeOffset RecibidoEn);
