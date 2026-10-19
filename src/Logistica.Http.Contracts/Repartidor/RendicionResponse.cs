namespace Logistica.Http.Contracts.Repartidor;

public sealed record BultoRendicionResponse(Guid EnvioId, string NumeroEnvio, Guid BultoId, string Codigo,
    string Situacion, bool RequiereDeclaracion);
public sealed record PreparacionRendicionResponse(IReadOnlyList<BultoRendicionResponse> Bultos, RendicionResponse? Iniciada);
public sealed record RendicionResponse(Guid Id, Guid RutaId, DateTimeOffset IniciadaEn, DateTimeOffset? FinalizadaEn,
    string? Observaciones, IReadOnlyList<LineaRendicionResponse> Lineas);
public sealed record LineaRendicionResponse(Guid EnvioId, Guid BultoId, string Situacion, bool Declarada,
    string? Observacion, bool? Recibida, string? EstadoFinal);
public sealed record IniciarRendicionRequest(IReadOnlyList<DeclaracionBultoRequest> Bultos,
    IReadOnlyList<Guid> Dependencias, string? Observaciones);
public sealed record DeclaracionBultoRequest(Guid BultoId, string Situacion, bool Devuelve, string? Observacion);
