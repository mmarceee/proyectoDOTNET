namespace Logistica.Modules.Envios.Domain.Envios;

// RF 33 y CU-32: un recargo de nueva visita con el número de su envío, sólo para leer (lo arma el repositorio).
internal sealed record RecargoRegistrado(Guid RecargoId, Guid EnvioId, string Numero, DateTimeOffset OcurridoEn, decimal Monto);
