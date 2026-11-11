namespace Logistica.Modules.Envios.Contracts.Results;

public sealed record CustodiaBulto(Guid BultoId, Guid EnvioId, string Codigo, string Estado, string Ubicacion,
    Guid? RutaId, long Revision, DateTimeOffset? ConfirmadaEn);
public sealed record ConfirmacionCustodiaBulto(Guid BultoId, long RevisionEsperada);
public sealed record ConciliacionBultos(Guid OperacionId, Guid? RutaId, long? RevisionRuta,
    IReadOnlyList<ConfirmacionCustodiaBulto> Bultos, bool EnDeposito, string Motivo, Guid? ResponsableId);
