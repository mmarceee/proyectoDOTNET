namespace Logistica.Http.Contracts.Deposito;

// Cuerpo de POST /api/deposito/recepciones (CU-30). Las medidas son opcionales.
public sealed record RecibirBultoRequest(
    string CodigoBulto,
    decimal? PesoKg = null,
    decimal? LargoCm = null,
    decimal? AnchoCm = null,
    decimal? AltoCm = null);
