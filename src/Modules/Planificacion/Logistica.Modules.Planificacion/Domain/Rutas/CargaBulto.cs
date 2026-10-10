namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed record CargaBulto(Guid EnvioId, Guid BultoId, decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm)
{
    public decimal VolumenM3 => LargoCm * AnchoCm * AltoCm / 1_000_000m;
}
