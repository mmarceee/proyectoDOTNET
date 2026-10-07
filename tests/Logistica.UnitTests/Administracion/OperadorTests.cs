using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

public class OperadorTests
{
    [Fact]
    public void Un_operador_nuevo_queda_activo()
    {
        var operador = Operador.Crear("Rápido SA", "rapido", "America/Montevideo");

        Assert.True(operador.Activo);
        Assert.NotEqual(Guid.Empty, operador.Id);
    }

    [Fact]
    public void Un_operador_creado_con_un_id_conserva_ese_id()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var operador = Operador.Crear("Rápido SA", "rapido", "America/Montevideo", id);

        Assert.Equal(id, operador.Id);
    }

    [Theory]
    [InlineData("", "rapido", "America/Montevideo")]
    [InlineData("Rápido SA", " ", "America/Montevideo")]
    [InlineData("Rápido SA", "rapido", "")]
    public void Un_operador_sin_datos_obligatorios_no_se_puede_crear(string nombre, string slug, string zonaHoraria)
    {
        Assert.Throws<DomainException>(() => Operador.Crear(nombre, slug, zonaHoraria));
    }
}
