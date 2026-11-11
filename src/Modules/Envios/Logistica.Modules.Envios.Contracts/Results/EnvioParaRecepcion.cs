namespace Logistica.Modules.Envios.Contracts.Results;

// Lo que Depósito necesita saber para recibir sus bultos (CU-30). PendienteDeRecepcion se refiere al bulto consultado.
public sealed record EnvioParaRecepcion(
    Guid EnvioId,
    string Numero,
    bool PendienteDeRecepcion,
    bool Cancelado,
    IReadOnlyList<BultoDeclarado> Bultos);

// Lo que el comercio declaró de cada bulto, para compararlo con lo que se mide en el depósito.
public sealed record BultoDeclarado(
    Guid BultoId,
    string Codigo,
    decimal PesoKg,
    decimal LargoCm,
    decimal AnchoCm,
    decimal AltoCm);
