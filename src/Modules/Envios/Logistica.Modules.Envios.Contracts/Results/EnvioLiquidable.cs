namespace Logistica.Modules.Envios.Contracts.Results;

// CU-32: un envío que llegó a Entregado o Devuelto, con la tarifa calculada al admitirlo. Estado es "Entregado" o
// "Devuelto"; LlegoEn es el instante de esa transición.
public sealed record EnvioLiquidable(Guid EnvioId, string Numero, string Estado, DateTimeOffset LlegoEn, decimal MontoTarifa);
