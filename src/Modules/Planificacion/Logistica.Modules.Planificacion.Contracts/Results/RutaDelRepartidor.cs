namespace Logistica.Modules.Planificacion.Contracts.Results;

public sealed record RutaDelRepartidor(Guid Id, DateOnly Fecha, string Estado, Guid VehiculoId,
    int Paradas, IReadOnlyList<Guid> Envios);
