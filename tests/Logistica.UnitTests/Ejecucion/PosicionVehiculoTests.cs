using Logistica.Modules.Ejecucion.Domain.Posiciones;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Ejecucion;

public class PosicionVehiculoTests
{
    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    public void Conserva_la_identidad_de_operacion_y_las_fechas_en_utc(decimal latitud, decimal longitud)
    {
        var id = Guid.NewGuid(); var captura = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.FromHours(-3));
        var p = PosicionVehiculo.Crear(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), latitud, longitud, captura, captura.AddHours(2));
        Assert.Equal(id, p.Id); Assert.Equal(latitud, p.Latitud); Assert.Equal(longitud, p.Longitud);
        Assert.Equal(TimeSpan.Zero, p.CapturadaEn.Offset); Assert.Equal(captura.ToUniversalTime(), p.CapturadaEn);
        Assert.Equal(captura.AddHours(2).ToUniversalTime(), p.RecibidaEn);
    }
    [Theory]
    [InlineData(-90.000001, 0)]
    [InlineData(90.000001, 0)]
    [InlineData(0, -180.000001)]
    [InlineData(0, 180.000001)]
    public void No_admite_coordenadas_fuera_de_rango(decimal latitud, decimal longitud) =>
        Assert.Throws<DomainException>(() => PosicionVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), latitud, longitud, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    [Fact]
    public void Exige_identidades_y_fecha_de_captura()
    {
        Assert.Throws<DomainException>(() => PosicionVehiculo.Crear(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => PosicionVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 0, 0, default, DateTimeOffset.UtcNow));
    }
}
