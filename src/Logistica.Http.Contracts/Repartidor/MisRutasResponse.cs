namespace Logistica.Http.Contracts.Repartidor;

public sealed record MisRutasResponse(DateOnly FechaOperativa, IReadOnlyList<TarjetaRutaResponse> Rutas);
public sealed record TarjetaRutaResponse(Guid RutaId, DateOnly FechaRuta, string Estado, string? Matricula,
    int Paradas, int Envios, bool CargaPendiente, bool RendicionIniciada, bool RendicionCerrada);
