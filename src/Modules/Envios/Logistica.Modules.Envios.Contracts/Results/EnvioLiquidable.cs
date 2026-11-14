namespace Logistica.Modules.Envios.Contracts.Results;

// CU-32: un envío terminado que se cobra (entrega parcial, cierre técnico D8), con la tarifa calculada al admitirlo, una
// sola vez. Estado es el cierre: "Entregado" (todos los bultos entregados), "Devuelto" (ninguno entregado) o
// "EntregadoParcialmente" (unos entregados y otros devueltos o extraviados). LlegoEn es cuándo terminó su último bulto.
public sealed record EnvioLiquidable(Guid EnvioId, string Numero, string Estado, DateTimeOffset LlegoEn, decimal MontoTarifa);
