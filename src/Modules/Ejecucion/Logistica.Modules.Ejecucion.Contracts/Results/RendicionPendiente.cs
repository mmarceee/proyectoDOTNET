namespace Logistica.Modules.Ejecucion.Contracts.Results;

public sealed record RendicionPendiente(Guid Id, Guid RutaId, Guid RepartidorId, DateTimeOffset IniciadaEn,
    DateTimeOffset? FinalizadaEn, string? Observaciones, IReadOnlyList<LineaRendicionPendiente> Lineas);

// DebeVolver: el bulto tenía que volver al depósito, así que CU-55 lo compara con lo escaneado. Recibida y EstadoFinal
// quedan en null hasta que CU-55 confirma.
public sealed record LineaRendicionPendiente(Guid EnvioId, Guid BultoId, string Situacion, bool Declarada,
    string? Observacion, bool? Recibida, string? EstadoFinal, bool DebeVolver);
