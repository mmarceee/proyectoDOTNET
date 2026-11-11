namespace Logistica.Http.Contracts.Planificacion;

public sealed record EnvioDisponibleResponse(Guid Id, string Numero, string Direccion, Guid? ZonaId, Guid? FranjaId,
    DateOnly? FechaProgramada, decimal PesoKg, decimal VolumenM3,
    DateTimeOffset? ReintentoDesde = null, bool DisponibleParaFecha = true, string Modalidad = "Estandar")
{
    public IReadOnlyList<BultoDisponibleResponse> Bultos { get; init; } = [];
}
public sealed record BultoDisponibleResponse(Guid Id, string Codigo, string Estado, decimal PesoKg, long RevisionCustodia);
