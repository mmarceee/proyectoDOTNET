using Logistica.BuildingBlocks.Infrastructure.Persistence;

namespace Logistica.UnitTests.Ejecucion;

public class InquilinoTrabajoTests
{
    [Fact]
    public void Sin_fijar_falla_cerrado_y_no_admite_cambiar_de_operador_en_el_mismo_ambito()
    {
        var trabajo = new InquilinoTrabajo(); Assert.Null(trabajo.OperadorId); Assert.Null(trabajo.ComercioId);
        Assert.Throws<InvalidOperationException>(() => trabajo.Fijar(Guid.Empty));
        var id = Guid.NewGuid(); trabajo.Fijar(id); Assert.Equal(id, trabajo.OperadorId);
        Assert.Throws<InvalidOperationException>(() => trabajo.Fijar(Guid.NewGuid()));
        Assert.Equal(id, trabajo.OperadorId);
    }
}
