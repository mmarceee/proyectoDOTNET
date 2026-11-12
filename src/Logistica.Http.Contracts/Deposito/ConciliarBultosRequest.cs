namespace Logistica.Http.Contracts.Deposito;

public sealed record BultoAConciliarRequest(Guid BultoId, long RevisionEsperada);
public sealed record ConciliarBultosRequest(Guid OperacionId, Guid? RutaId, long? RevisionRuta,
    IReadOnlyList<BultoAConciliarRequest> Bultos, bool EnDeposito, string Motivo);
public sealed record CustodiaBultoResponse(Guid BultoId, Guid EnvioId, string Codigo, string Estado, string Ubicacion,
    long Revision, Guid? RutaId, long? RevisionRuta);
