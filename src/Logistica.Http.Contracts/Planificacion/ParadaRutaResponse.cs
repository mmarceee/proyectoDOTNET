namespace Logistica.Http.Contracts.Planificacion;

public sealed record ParadaRutaResponse(Guid Id, int Orden, string Estado, string Direccion,
    TimeOnly? Desde, TimeOnly? Hasta, IReadOnlyList<EnvioParadaResponse> Envios);
