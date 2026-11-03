namespace Logistica.Modules.Envios.Domain.Envios;

// CU-32: un envío que terminó en Entregado o Devuelto, con el instante de esa transición y la tarifa de su admisión.
internal sealed record EnvioCerrado(Guid EnvioId, string Numero, EstadoEnvio Estado, DateTimeOffset LlegoEn, decimal MontoTarifa);
