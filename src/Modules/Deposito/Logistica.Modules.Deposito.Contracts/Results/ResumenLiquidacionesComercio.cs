namespace Logistica.Modules.Deposito.Contracts.Results;

// CU-72: lo liquidado a un comercio en un período, para el reporte de liquidación por comercio. Sólo las liquidaciones
// emitidas o pagadas: un borrador todavía puede cambiar. Cobrado es lo de las pagadas; Pendiente, lo de las emitidas.
public sealed record ResumenLiquidacionesComercio(Guid ComercioId, int Liquidaciones, int Envios, decimal TotalNeto,
    decimal Cobrado, decimal Pendiente);
