namespace Logistica.Http.Contracts.Planificacion;

public sealed record EnvioDisponibleResponse(Guid Id, string Numero, string Direccion, Guid? ZonaId, Guid? FranjaId,
    DateOnly? FechaProgramada, decimal PesoKg, decimal VolumenM3,
    DateTimeOffset? ReintentoDesde = null, bool DisponibleParaFecha = true, string Modalidad = "Estandar");
