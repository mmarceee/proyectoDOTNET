namespace Logistica.Modules.Administracion.Contracts.Results;

public sealed record RecursosPlanificacion(IReadOnlyList<RepartidorPlanificacion> Repartidores,
    IReadOnlyList<VehiculoPlanificacion> Vehiculos, IReadOnlyList<ZonaPlanificacion> Zonas,
    IReadOnlyList<FranjaPlanificacion> Franjas);
