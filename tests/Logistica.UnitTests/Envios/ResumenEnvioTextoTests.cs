using Logistica.Modules.Envios.Presentation;

namespace Logistica.UnitTests.Envios;

// RF 31 y RF 35: la presentación esperada de los ejemplos de aceptación (decisiones-entrega-parcial.md, sección 3).
public class ResumenEnvioTextoTests
{
    [Theory]
    [InlineData("Creado", false, 0, 3, "Creado", "0 de 3 entregados")]
    [InlineData("EnOperacion", false, 0, 3, "En operación", "0 de 3 entregados")]
    [InlineData("EntregadoParcialmente", false, 2, 3, "Entregado parcialmente", "2 de 3 entregados · En curso")]
    [InlineData("EntregadoParcialmente", true, 2, 3, "Entregado parcialmente", "2 de 3 entregados · Finalizado")]
    [InlineData("Entregado", true, 3, 3, "Entregado", "3 de 3 entregados")]
    // Todos devueltos o extraviados, o todos cancelados antes de recibir: terminado sin ninguna entrega.
    [InlineData("EnOperacion", true, 0, 3, "Finalizado sin entrega", "0 de 3 entregados")]
    [InlineData("Creado", true, 0, 2, "Finalizado sin entrega", "0 de 2 entregados")]
    public void Muestra_el_resumen_y_el_contador_acordados(string resumen, bool terminado, int entregados, int total,
        string etiqueta, string detalle)
    {
        Assert.Equal(etiqueta, ResumenEnvioTexto.Mostrar(resumen, terminado, entregados));
        Assert.Equal(detalle, ResumenEnvioTexto.Detalle(resumen, terminado, entregados, total));
    }
}
