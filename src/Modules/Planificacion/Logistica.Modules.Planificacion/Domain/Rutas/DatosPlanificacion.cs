namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal enum EstadoRuta { Planificada, Despachada, EnCurso, Finalizada }
internal enum EstadoParada { Pendiente, Completada, Fallida }
internal enum OperacionValidacionRuta { Creacion, Agregado, Modificacion, Despacho, CambioFranjaProgramado }
internal sealed record CargaBulto(Guid EnvioId, Guid BultoId, decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm)
{
    public decimal VolumenM3 => LargoCm * AnchoCm * AltoCm / 1_000_000m;
}
internal sealed record VehiculoRuta(Guid Id, decimal PesoKg, decimal VolumenM3, decimal LargoCm, decimal AnchoCm, decimal AltoCm);
internal sealed record FranjaRuta(Guid Id, Guid ZonaId, TimeOnly Desde, TimeOnly Hasta, IReadOnlyList<DayOfWeek> Dias);
internal sealed record EnvioRuta(Guid Id, string Numero, DateOnly? Fecha, Guid? ZonaId, FranjaRuta? Franja,
    bool FranjaRequerida, IReadOnlyList<CargaBulto> Bultos);
internal sealed record RestriccionRuta(string Codigo, string Mensaje, Guid? EnvioId = null, Guid? BultoId = null);
internal sealed record EvaluacionRuta(decimal PesoKg, decimal VolumenM3, int Paradas, IReadOnlyList<RestriccionRuta> Restricciones)
{
    public bool Valida => Restricciones.Count == 0;
}
