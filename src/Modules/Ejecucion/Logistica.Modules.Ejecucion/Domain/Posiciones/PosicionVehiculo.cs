using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Domain.Posiciones;

internal sealed class PosicionVehiculo : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid VehiculoId { get; private set; }
    public Guid RutaId { get; private set; }
    public decimal Latitud { get; private set; }
    public decimal Longitud { get; private set; }
    public DateTimeOffset CapturadaEn { get; private set; }
    public DateTimeOffset RecibidaEn { get; private set; }
    private PosicionVehiculo() { }
    private PosicionVehiculo(Guid id) : base(id) { }
    public static bool CoordenadasValidas(decimal? latitud, decimal? longitud) =>
        latitud is >= -90 and <= 90 && longitud is >= -180 and <= 180;

    public static PosicionVehiculo Crear(Guid operacion, Guid operador, Guid vehiculo, Guid ruta,
        decimal latitud, decimal longitud, DateTimeOffset capturada, DateTimeOffset recibida)
    {
        if (new[] { operacion, operador, vehiculo, ruta }.Contains(Guid.Empty)
            || !CoordenadasValidas(latitud, longitud)
            || capturada == default || recibida == default)
            throw new DomainException("La posición del vehículo no es válida.");
        return new(operacion) { OperadorId = operador, VehiculoId = vehiculo, RutaId = ruta,
            Latitud = latitud, Longitud = longitud, CapturadaEn = capturada.ToUniversalTime(), RecibidaEn = recibida.ToUniversalTime() };
    }
}
