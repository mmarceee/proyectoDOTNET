namespace Logistica.Http.Contracts.Envios;

public sealed record ImprimirEtiquetasRequest(IReadOnlyList<string>? Numeros);
