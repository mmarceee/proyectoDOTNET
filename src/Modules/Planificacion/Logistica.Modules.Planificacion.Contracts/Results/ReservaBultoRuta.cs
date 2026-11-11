namespace Logistica.Modules.Planificacion.Contracts.Results;

public sealed record ReservaBultoRuta(Guid BultoId, Guid EnvioId, Guid RutaId, Guid ParadaId,
    long Revision, DateTimeOffset? CerradaEn, string? CausaCierre, DateTimeOffset AbiertaEn = default);
