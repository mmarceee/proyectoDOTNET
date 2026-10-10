namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed record FranjaRuta(Guid Id, Guid ZonaId, TimeOnly Desde, TimeOnly Hasta, IReadOnlyList<DayOfWeek> Dias);
