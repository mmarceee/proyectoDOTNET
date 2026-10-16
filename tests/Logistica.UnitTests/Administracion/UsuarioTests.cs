using Logistica.Modules.Administracion.Infrastructure.Identity;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

// CU-02: las reglas del usuario.
public class UsuarioTests
{
    [Fact]
    public void Un_usuario_nuevo_queda_activo_y_pendiente_de_activacion()
    {
        var usuario = Usuario.CrearPersonal(Guid.NewGuid(), "Ana", "ana@operador.uy");

        Assert.True(usuario.Activo);
        Assert.True(usuario.PendienteDeActivacion);
        Assert.True(usuario.EsPersonal);
    }

    [Fact]
    public void El_personal_se_puede_desactivar()
    {
        var usuario = Usuario.CrearPersonal(Guid.NewGuid(), "Ana", "ana@operador.uy");

        usuario.Desactivar();

        Assert.False(usuario.Activo);
    }

    // A4: el usuario de comercio lo comparten todos sus operadores; se suspende la relación, no el usuario.
    [Fact]
    public void Un_usuario_de_comercio_no_se_desactiva()
    {
        var usuario = Usuario.CrearDeComercio(Guid.NewGuid(), "Tienda", "tienda@comercio.uy");

        Assert.False(usuario.EsPersonal);
        Assert.Throws<DomainException>(() => usuario.Desactivar());
    }

    // A2: datos inválidos.
    [Theory]
    [InlineData("", "ana@operador.uy")]
    [InlineData("Ana", "")]
    public void Un_usuario_sin_nombre_o_sin_correo_no_se_crea(string nombre, string email)
    {
        Assert.Throws<DomainException>(() => Usuario.CrearPersonal(Guid.NewGuid(), nombre, email));
    }

    [Fact]
    public void El_personal_necesita_un_operador_y_el_usuario_de_comercio_un_comercio()
    {
        Assert.Throws<DomainException>(() => Usuario.CrearPersonal(Guid.Empty, "Ana", "ana@operador.uy"));
        Assert.Throws<DomainException>(() => Usuario.CrearDeComercio(Guid.Empty, "Tienda", "tienda@comercio.uy"));
    }
}
