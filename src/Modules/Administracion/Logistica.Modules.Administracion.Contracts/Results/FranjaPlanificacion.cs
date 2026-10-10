namespace Logistica.Modules.Administracion.Contracts.Results;

public sealed record FranjaPlanificacion(Guid ReferenciaId, Guid Id, Guid ZonaId, TimeOnly Desde, TimeOnly Hasta,
    IReadOnlyList<DayOfWeek> Dias, DateOnly VigenteDesde, DateOnly? VigenteHasta);
