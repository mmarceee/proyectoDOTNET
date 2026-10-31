namespace Logistica.Modules.Envios.Contracts.Results;

// CU-31, paso 2: la devolución de un envío con sus bultos, para que el depósito la entregue al comercio.
// RecibidaEnDeposito: los bultos ya volvieron en la rendición (CU-55); recién entonces se puede entregar.
public sealed record DevolucionParaEntrega(Guid EnvioId, string Numero, string EstadoEnvio, string Motivo,
    bool RecibidaEnDeposito, bool Cerrada, IReadOnlyList<BultoDevuelto> Bultos);

public sealed record BultoDevuelto(Guid Id, string Codigo);

// CU-31, pasos 3 a 5: quién recibe por el comercio y qué bultos se le entregan. Los que no se entregan quedan como
// incidencia (A2).
public sealed record DevolucionAConfirmar(Guid EnvioId, string NombreReceptor, string DocumentoReceptor,
    IReadOnlyCollection<Guid> BultosEntregados, Guid? ResponsableId);
