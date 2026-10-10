namespace Logistica.Http.Contracts.Planificacion;

public sealed record EvaluacionRutaResponse(bool Valida, decimal PesoKg, decimal VolumenM3, int Paradas,
    decimal CapacidadPesoKg, decimal CapacidadVolumenM3, int MaxParadas,
    IReadOnlyList<RestriccionRutaResponse> Restricciones);
