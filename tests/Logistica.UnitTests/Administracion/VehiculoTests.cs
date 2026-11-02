using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

// CU-08: reglas de un vehículo solo. A1 (matrícula única) y A3 (rutas en curso) comparan con otros datos: se prueban
// en la prueba de integración.
public class VehiculoTests
{
    private static Vehiculo UnVehiculo() => Vehiculo.Crear(Guid.NewGuid(), " abc 1234 ", "Furgón", 800, 6, 300, 170, 160);

    [Fact]
    public void Un_vehiculo_nuevo_queda_activo_con_la_matricula_en_mayusculas()
    {
        var vehiculo = UnVehiculo();

        Assert.True(vehiculo.Activo);
        Assert.Equal("ABC 1234", vehiculo.Matricula);
    }

    // A2: toda capacidad y dimensión tiene que ser mayor que cero, al crear y al modificar.
    [Theory]
    [InlineData(0, 6, 300, 170, 160)]
    [InlineData(800, -1, 300, 170, 160)]
    [InlineData(800, 6, 300, 0, 160)]
    public void Una_capacidad_que_no_es_positiva_se_rechaza(decimal peso, decimal volumen, decimal largo, decimal ancho, decimal alto)
    {
        Assert.Throws<DomainException>(() => Vehiculo.Crear(Guid.NewGuid(), "ABC 1234", "Furgón", peso, volumen, largo, ancho, alto));
        Assert.Throws<DomainException>(() => UnVehiculo().Modificar("Furgón", peso, volumen, largo, ancho, alto));
    }

    [Fact]
    public void Si_la_modificacion_se_rechaza_el_vehiculo_queda_como_estaba()
    {
        var vehiculo = UnVehiculo();

        Assert.Throws<DomainException>(() => vehiculo.Modificar("Camión", 0, 10, 400, 200, 200));

        Assert.Equal("Furgón", vehiculo.Tipo);
        Assert.Equal(800, vehiculo.CapacidadPesoKg);
    }

    [Fact]
    public void Se_modifican_el_tipo_y_las_capacidades_y_la_matricula_no_cambia()
    {
        var vehiculo = UnVehiculo();

        vehiculo.Modificar("Camión", 2000, 12, 450, 220, 210);

        Assert.Equal("Camión", vehiculo.Tipo);
        Assert.Equal(2000, vehiculo.CapacidadPesoKg);
        Assert.Equal("ABC 1234", vehiculo.Matricula);
    }

    [Fact]
    public void Un_vehiculo_desactivado_queda_inactivo()
    {
        var vehiculo = UnVehiculo();

        vehiculo.Desactivar();

        Assert.False(vehiculo.Activo);
    }
}
