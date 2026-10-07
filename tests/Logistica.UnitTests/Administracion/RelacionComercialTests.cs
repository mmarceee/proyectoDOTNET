using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

public class RelacionComercialTests
{
    private static readonly Guid OperadorId = Guid.NewGuid();
    private static readonly Guid ComercioId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Una_relacion_nueva_queda_activa_con_su_operador_y_su_comercio()
    {
        var relacion = RelacionComercial.Crear(OperadorId, ComercioId, "envios@tienda.uy", Ahora);

        Assert.Equal(EstadoRelacion.Activa, relacion.Estado);
        Assert.Equal(OperadorId, relacion.OperadorId);
        Assert.Equal(ComercioId, relacion.ComercioId);
        Assert.Equal(Ahora, relacion.FechaAlta);
    }

    [Fact]
    public void Una_relacion_sin_correo_de_contacto_no_se_puede_crear()
    {
        Assert.Throws<DomainException>(() => RelacionComercial.Crear(OperadorId, ComercioId, "", Ahora));
    }

    [Fact]
    public void Un_comercio_sin_documento_fiscal_no_se_puede_crear()
    {
        Assert.Throws<DomainException>(() => Comercio.Crear("Tienda Pepe SA", ""));
    }
}
