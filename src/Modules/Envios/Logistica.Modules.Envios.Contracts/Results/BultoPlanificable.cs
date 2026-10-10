namespace Logistica.Modules.Envios.Contracts.Results;

public sealed record BultoPlanificable(Guid Id, decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm);
