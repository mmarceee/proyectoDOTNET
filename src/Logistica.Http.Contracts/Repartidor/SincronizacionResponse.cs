namespace Logistica.Http.Contracts.Repartidor;

// Respuesta de POST /api/repartidor/sincronizacion: el resultado de cada operación del lote (ADR-0004, sección 2.4).
public sealed record SincronizacionResponse(IReadOnlyList<ResultadoOperacionResponse> Resultados);

// Resultado: "Aplicada", "YaAplicada", "Conflicto" o "Invalida". Motivo: código del conflicto o de la invalidez
// (por ejemplo, "EnvioExtraviado" o "TipoNoSoportado"). La PWA borra de su cola las Aplicada y YaAplicada.
public sealed record ResultadoOperacionResponse(Guid OperacionId, string Resultado, string? Motivo = null, string? Detalle = null);
