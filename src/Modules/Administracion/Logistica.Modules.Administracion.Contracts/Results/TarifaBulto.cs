namespace Logistica.Modules.Administracion.Contracts.Results;

// El precio de un bulto y la versión del cuadro tarifario con que se calculó (CU-05; CU-10, paso 7). El envío guarda la
// versión: si después se publica otra, conserva su precio (letra 6.6).
public sealed record TarifaBulto(Guid VersionTarifarioId, int VersionTarifarioNumero, decimal Monto);
