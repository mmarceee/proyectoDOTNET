namespace Logistica.Modules.Administracion.Contracts.Results;

public sealed record VehiculoPlanificacion(Guid Id, string Matricula, bool Activo, decimal CapacidadPesoKg,
    decimal CapacidadVolumenM3, decimal LargoCargaCm, decimal AnchoCargaCm, decimal AltoCargaCm);
