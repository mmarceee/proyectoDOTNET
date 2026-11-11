namespace Logistica.Modules.Envios.Contracts.Results;

public sealed record BultoPlanificable(Guid Id, decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm)
{
    public string Codigo { get; init; } = "";
    public string Estado { get; init; } = "";
    public string Custodia { get; init; } = "Desconocida";
    public long RevisionCustodia { get; init; }
    public DateTimeOffset? ReintentoDesde { get; init; }
}
