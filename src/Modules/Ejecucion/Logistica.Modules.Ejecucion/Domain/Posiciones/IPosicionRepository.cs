namespace Logistica.Modules.Ejecucion.Domain.Posiciones;

internal interface IPosicionRepository
{
    void Agregar(PosicionVehiculo posicion);
    Task<int> EliminarAnterioresAsync(DateTimeOffset desde, CancellationToken ct);
    Task<IReadOnlyList<PosicionVehiculo>> UltimasAsync(IReadOnlyCollection<Guid> vehiculos, DateTimeOffset desde, CancellationToken ct);
}
