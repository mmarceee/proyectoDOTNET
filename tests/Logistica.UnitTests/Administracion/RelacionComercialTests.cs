using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

public class RelacionComercialTests
{
    private static RelacionComercial Nueva() => RelacionComercial.Crear(Guid.NewGuid(), Guid.NewGuid(), "contacto@tienda.uy", DateTimeOffset.UtcNow);

    [Fact]
    public void Suspension_y_reactivacion_solo_desde_el_estado_correspondiente()
    {
        var relacion = Nueva();
        Assert.Throws<DomainException>(() => relacion.Reactivar());
        relacion.Suspender();
        Assert.Throws<DomainException>(() => relacion.Suspender());
        relacion.Reactivar();
        Assert.Equal(EstadoRelacion.Activa, relacion.Estado);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Baja_desde_activa_o_suspendida_y_sin_envios_pendientes(bool suspendida)
    {
        var relacion = Nueva();
        if (suspendida) relacion.Suspender();
        Assert.Throws<DomainException>(() => relacion.DarDeBaja(2));
        Assert.Equal(suspendida ? EstadoRelacion.Suspendida : EstadoRelacion.Activa, relacion.Estado);
        relacion.DarDeBaja(0);
        Assert.Equal(EstadoRelacion.Baja, relacion.Estado);
        Assert.Throws<DomainException>(() => relacion.Reactivar());
        Assert.Throws<DomainException>(() => relacion.Suspender());
        Assert.Throws<DomainException>(() => relacion.ModificarContacto("otro@tienda.uy"));
        Assert.Throws<DomainException>(() => relacion.DarDeBaja(0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-correo")]
    [InlineData("Nombre <contacto@tienda.uy>")]
    public void Correo_invalido_no_cambia_la_relacion(string correo)
    {
        var relacion = Nueva();
        Assert.Throws<DomainException>(() => relacion.ModificarContacto(correo));
        Assert.Equal("contacto@tienda.uy", relacion.EmailContacto);
    }
}
