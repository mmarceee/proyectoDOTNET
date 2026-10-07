namespace Logistica.Http.Contracts.Deposito;

public sealed record RecibirBultoResponse(
    string NumeroEnvio,
    string Resultado,
    string? Discrepancia,
    IReadOnlyList<BultoRecepcionResponse> Bultos,
    bool EnvioEnDeposito);

public sealed record BultoRecepcionResponse(string Codigo, bool Recibido);
