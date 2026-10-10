namespace Logistica.Http.Contracts.Planificacion;

public sealed record ValidacionRutaResponse(Guid Id, long RevisionRuta, DateTimeOffset ValidadaEn, Guid? ResponsableId,
    string Operacion, DateOnly FechaRuta, Guid RepartidorId, Guid VehiculoId, Guid VersionReglasId, int MaxParadas,
    decimal CapacidadPesoKg, decimal CapacidadVolumenM3, decimal LargoCargaCm, decimal AnchoCargaCm, decimal AltoCargaCm,
    IReadOnlyList<BultoValidadoResponse> Bultos);
