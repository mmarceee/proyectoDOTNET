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
    [InlineData("NoEntregado", true)]
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
}
