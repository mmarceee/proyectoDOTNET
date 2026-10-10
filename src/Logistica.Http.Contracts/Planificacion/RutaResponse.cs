namespace Logistica.Http.Contracts.Planificacion;

public sealed record RutaResponse(Guid Id, DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, string Estado,
    long Revision, bool ReservaActiva, IReadOnlyList<ParadaRutaResponse> Paradas, EvaluacionRutaResponse? Carga = null);
