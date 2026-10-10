namespace Logistica.Http.Contracts.Planificacion;

public sealed record ParadaRutaResponse(Guid Id, Guid EnvioId, int Orden, string Estado);
