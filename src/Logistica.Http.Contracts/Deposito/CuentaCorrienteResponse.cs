namespace Logistica.Http.Contracts.Deposito;

// Respuestas de GET /api/cuenta-corriente (CU-33). Las usan el endpoint y el Portal del comercio.
public sealed record CuentaCorrienteResponse(decimal SaldoPendiente, IReadOnlyList<LiquidacionResumenResponse> Liquidaciones);

public sealed record LiquidacionResumenResponse(
    Guid Id,
    DateOnly PeriodoDesde,
    DateOnly PeriodoHasta,
    string Estado,
    decimal TotalBruto,
    decimal TotalAjustes,
    decimal TotalNeto,
    DateTimeOffset? EmitidaEn,
    DateTimeOffset? PagadaEn);

public sealed record LiquidacionDetalleResponse(LiquidacionResumenResponse Liquidacion,
    IReadOnlyList<LineaLiquidacionResponse> Lineas);

// La fecha es el día, en la zona del operador, en que el envío llegó a Entregado o Devuelto.
public sealed record LineaLiquidacionResponse(string NumeroEnvio, DateOnly Fecha, string Concepto, decimal Monto);
