namespace Logistica.Modules.Envios.Contracts.Results;

// RF 33 y CU-32: el cargo de una visita adicional, con el monto registrado al realizarla. La liquidación lo cobra tal
// cual, sin recalcularlo con las reglas de hoy (decisiones-entrega-parcial.md, sección 5).
public sealed record RecargoLiquidable(Guid RecargoId, Guid EnvioId, string Numero, DateTimeOffset OcurridoEn, decimal Monto);
