namespace Logistica.Http.Contracts.Planificacion;

public sealed record EnvioDisponibleResponse(Guid Id, string Numero, string Direccion, Guid? ZonaId, Guid? FranjaId,
    DateOnly? FechaProgramada, decimal PesoKg, decimal VolumenM3);
