using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

public class RepartidorTests
{
    private static Repartidor UnRepartidor() => Repartidor.Crear(Guid.NewGuid(), "Ana", "DOC-1");

    [Fact]
    public void Un_repartidor_nuevo_no_tiene_usuario()
    {
        Assert.Null(UnRepartidor().UsuarioId);
    }

    [Fact]
    public void Se_vincula_con_un_usuario_y_volver_a_vincularlo_con_el_mismo_no_cambia_nada()
    {
        var repartidor = UnRepartidor();
        var usuario = Guid.NewGuid();

        repartidor.VincularUsuario(usuario);
        repartidor.VincularUsuario(usuario);

        Assert.Equal(usuario, repartidor.UsuarioId);
    }

    [Fact]
    public void Un_repartidor_ya_vinculado_no_se_vincula_con_otro_usuario()
    {
        var repartidor = UnRepartidor();
        repartidor.VincularUsuario(Guid.NewGuid());

        Assert.Throws<DomainException>(() => repartidor.VincularUsuario(Guid.NewGuid()));
    }

    [Fact]
    public void El_vinculo_necesita_un_usuario()
    {
        Assert.Throws<DomainException>(() => UnRepartidor().VincularUsuario(Guid.Empty));
    }

    // CU-08: se modifica el nombre; el documento queda igual.
    [Fact]
    public void Se_modifica_el_nombre_y_el_documento_no_cambia()
    {
        var repartidor = UnRepartidor();

        repartidor.Modificar("  Ana López ");

        Assert.Equal("Ana López", repartidor.Nombre);
        Assert.Equal("DOC-1", repartidor.Documento);
    }

    [Fact]
    public void El_nombre_no_puede_quedar_vacio()
    {
        Assert.Throws<DomainException>(() => UnRepartidor().Modificar(" "));
    }

    [Fact]
    public void Un_repartidor_desactivado_queda_inactivo()
    {
        var repartidor = UnRepartidor();

        repartidor.Desactivar();

        Assert.False(repartidor.Activo);
    }
}
