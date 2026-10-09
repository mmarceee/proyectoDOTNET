namespace Logistica.Modules.Envios.Contracts.Results;

public sealed record EnvioPlanificable(Guid Id, Guid OperadorId, string Numero, string Estado, Guid? ZonaId,
    Guid? FranjaHorariaId, DateOnly? FechaEntregaProgramada, string Direccion, string CodigoPostal,
    IReadOnlyList<BultoPlanificable> Bultos);
public sealed record BultoPlanificable(Guid Id, decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm);
public sealed record AsignacionEnvioRuta(Guid EnvioId, Guid? FranjaId);
