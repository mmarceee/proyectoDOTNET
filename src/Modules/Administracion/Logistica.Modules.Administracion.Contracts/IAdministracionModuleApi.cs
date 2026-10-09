namespace Logistica.Modules.Administracion.Contracts;

public interface IAdministracionModuleApi
{
    Task BloquearPlanificacionAsync(CancellationToken ct);
    Task<ContextoPlanificacion> ObtenerContextoPlanificacionAsync(CancellationToken ct);
    Task<RecursosPlanificacion> ConsultarRecursosPlanificacionAsync(DateOnly fecha, CancellationToken ct);
    Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct);
    Task<IReadOnlyList<FranjaPlanificacion>> ResolverFranjasAsync(IReadOnlyCollection<Guid> ids, DateOnly fecha, CancellationToken ct);
}

public sealed record ContextoPlanificacion(Guid OperadorId, string ZonaHoraria);
public sealed record RepartidorPlanificacion(Guid Id, string Nombre, bool Activo);
public sealed record VehiculoPlanificacion(Guid Id, string Matricula, bool Activo, decimal CapacidadPesoKg,
    decimal CapacidadVolumenM3, decimal LargoCargaCm, decimal AnchoCargaCm, decimal AltoCargaCm);
public sealed record ZonaPlanificacion(Guid Id, string Nombre, IReadOnlyList<string> CodigosPostales);
public sealed record FranjaPlanificacion(Guid ReferenciaId, Guid Id, Guid ZonaId, TimeOnly Desde, TimeOnly Hasta,
    IReadOnlyList<DayOfWeek> Dias, DateOnly VigenteDesde, DateOnly? VigenteHasta);
public sealed record RecursosPlanificacion(IReadOnlyList<RepartidorPlanificacion> Repartidores,
    IReadOnlyList<VehiculoPlanificacion> Vehiculos, IReadOnlyList<ZonaPlanificacion> Zonas,
    IReadOnlyList<FranjaPlanificacion> Franjas);
public sealed record ReglasPlanificacion(Guid VersionId, int MaxParadasPorRuta);
