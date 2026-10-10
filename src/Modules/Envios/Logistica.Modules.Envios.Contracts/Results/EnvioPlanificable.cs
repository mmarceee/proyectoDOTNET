namespace Logistica.Modules.Envios.Contracts.Results;

public sealed record EnvioPlanificable(Guid Id, Guid OperadorId, string Numero, string Estado, Guid? ZonaId,
    Guid? FranjaHorariaId, DateOnly? FechaEntregaProgramada, string Direccion, string CodigoPostal,
    IReadOnlyList<BultoPlanificable> Bultos);
