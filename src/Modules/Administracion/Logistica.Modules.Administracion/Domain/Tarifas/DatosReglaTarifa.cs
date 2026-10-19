using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Domain.Tarifas;

// Los valores de una regla del cuadro tarifario: con ellos se crea y se copia a un borrador nuevo (CU-05, paso 4).
internal sealed record DatosReglaTarifa(Guid ZonaId, Modalidad Modalidad, decimal PesoDesdeKg, decimal PesoHastaKg,
    decimal VolumenDesdeM3, decimal VolumenHastaM3, decimal PrecioBase);
