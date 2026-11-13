namespace Logistica.Modules.Envios.Domain.Envios;

// CU-32: un envío terminado que se cobra (cierre técnico, D8): todos sus bultos en un estado terminal y al menos uno
// entregado o devuelto. Cierre es "Entregado" (todos entregados), "Devuelto" (ninguno entregado) o
// "EntregadoParcialmente" (cierre mixto). LlegoEn es TerminadoEn; MontoTarifa, la tarifa de su admisión, una sola vez.
internal sealed record EnvioCerrado(Guid EnvioId, string Numero, string Cierre, DateTimeOffset LlegoEn, decimal MontoTarifa);
