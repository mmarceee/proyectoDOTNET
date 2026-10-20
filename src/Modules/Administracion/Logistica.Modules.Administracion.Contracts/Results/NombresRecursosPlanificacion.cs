namespace Logistica.Modules.Administracion.Contracts.Results;

public sealed record NombresRecursosPlanificacion(IReadOnlyDictionary<Guid, string> Repartidores,
    IReadOnlyDictionary<Guid, string> Vehiculos);
