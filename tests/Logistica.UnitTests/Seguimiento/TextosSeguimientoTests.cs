using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;

namespace Logistica.UnitTests.Seguimiento;

// CU-60: lo que ve la destinataria (paso 3 y requerimiento 5).
public class TextosSeguimientoTests
{
    private static readonly FranjaPlanificacion Manana =
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(9, 0), new(13, 0), [], new(2026, 1, 1), null);

    [Fact]
    public void Con_fecha_y_franja_la_ventana_es_ese_dia_y_ese_horario()
    {
        var ventana = TextosSeguimiento.Ventana("EnDeposito", new DateOnly(2026, 11, 12), Manana);

        Assert.Equal("El jueves 12 de noviembre, entre las 09:00 y las 13:00", ventana);
    }

    [Theory]
    [InlineData("Entregado")]
    [InlineData("Cancelado")]
    [InlineData("Devuelto")]
    public void Un_envio_terminado_no_tiene_ventana(string estado)
    {
        Assert.Null(TextosSeguimiento.Ventana(estado, new DateOnly(2026, 11, 12), Manana));
    }

    [Fact]
    public void Sin_fecha_todavia_no_hay_ventana()
    {
        Assert.Null(TextosSeguimiento.Ventana("Admitido", null, null));
    }

    [Theory]
    [InlineData("Admitido", true)]
    [InlineData("EnDeposito", true)]
    [InlineData("AsignadoARuta", true)]
    [InlineData("Reprogramado", true)]
    [InlineData("NoEntregado", false)] // transitorio: después de un intento fallido ya está Reprogramado (CU-61, H1)
    [InlineData("EnTransito", false)]
    [InlineData("Entregado", false)]
    public void Se_ofrece_reprogramar_mientras_no_salio_a_reparto(string estado, bool puede)
    {
        Assert.Equal(puede, TextosSeguimiento.PuedeReprogramar(estado));
    }

    [Fact]
    public void Los_estados_se_muestran_en_palabras_de_la_destinataria()
    {
        Assert.Equal("En camino", TextosSeguimiento.Estado("EnTransito"));
        Assert.Equal("Recibimos tu envío", TextosSeguimiento.Estado("Admitido"));
    }


    // RF 35: con parte de los bultos entregados manda el resumen; si no, el estado de siempre.
    [Theory]
    [InlineData("Reprogramado", "EntregadoParcialmente", "Entregado parcialmente")]
    [InlineData("EnDevolucion", "EntregadoParcialmente", "Entregado parcialmente")]
    [InlineData("EnTransito", "EnOperacion", "En camino")]
    [InlineData("Entregado", "Entregado", "Entregado")]
    [InlineData("Devuelto", "EnOperacion", "Devuelto al comercio")]
    public void El_titulo_muestra_la_entrega_parcial_aunque_el_estado_diga_otra_cosa(
        string estado, string resumen, string titulo)
    {
        Assert.Equal(titulo, TextosSeguimiento.Titulo(estado, resumen));
    }

    // RF 35: el contador mide entregas efectivas sobre el total original (decisiones-entrega-parcial.md, sección 2).
    [Theory]
    [InlineData(2, 3, "2 de 3 bultos entregados")]
    [InlineData(0, 3, "0 de 3 bultos entregados")]
    [InlineData(3, 3, "3 de 3 bultos entregados")]
    public void El_avance_cuenta_los_entregados_sobre_el_total(int entregados, int total, string avance)
    {
        Assert.Equal(avance, TextosSeguimiento.Avance(entregados, total));
    }

    [Fact]
    public void Con_un_solo_bulto_no_hay_avance()
    {
        Assert.Null(TextosSeguimiento.Avance(1, 1));
    }

    // RF 35: sin el código de la etiqueta ni el Id (decisión del equipo del 07/11/2026).
    [Fact]
    public void Cada_bulto_se_nombra_por_su_numero()
    {
        Assert.Equal("BTO 3", TextosSeguimiento.NombreBulto(3));
    }
}
