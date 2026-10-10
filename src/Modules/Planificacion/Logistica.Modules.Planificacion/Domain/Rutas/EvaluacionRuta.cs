namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed record EvaluacionRuta(decimal PesoKg, decimal VolumenM3, int Paradas, IReadOnlyList<RestriccionRuta> Restricciones)
{
    public bool Valida => Restricciones.Count == 0;
}
