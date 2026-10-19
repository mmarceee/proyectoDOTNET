using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Domain.Tarifas;

// Los valores de un ajuste del cuadro tarifario: con ellos se crea y se copia a un borrador nuevo (CU-05, paso 4).
internal sealed record DatosAjusteTarifa(TipoAjuste Tipo, bool EsPorcentaje, decimal Valor, Modalidad Modalidad);
