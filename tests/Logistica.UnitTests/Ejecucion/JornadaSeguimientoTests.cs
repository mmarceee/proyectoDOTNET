using Logistica.Modules.Ejecucion.Domain.Posiciones;

namespace Logistica.UnitTests.Ejecucion;

public class JornadaSeguimientoTests
{
    private static readonly DateTimeOffset Salida = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(true, false, false, 10, ValidezCaptura.Valida)]
    [InlineData(false, false, false, 10, ValidezCaptura.RutaNoEnCurso)]
    [InlineData(false, true, false, 10, ValidezCaptura.RutaNoEnCurso)]
    [InlineData(false, true, true, 10, ValidezCaptura.Valida)]
    [InlineData(true, false, true, 30, ValidezCaptura.FueraDeJornada)]
    [InlineData(true, false, false, -1, ValidezCaptura.FueraDeJornada)]
    public void Evalua_el_periodo_de_captura_en_el_dominio(bool enCurso, bool finalizada, bool rendida,
        int minutos, object esperado)
    {
        var jornada = new JornadaSeguimiento(enCurso, finalizada, Salida, rendida ? Salida.AddMinutes(30) : null);
        Assert.Equal((ValidezCaptura)esperado, jornada.Evaluar(Salida.AddMinutes(minutos)));
        Assert.Equal(rendida || finalizada, jornada.SoloHistorial);
    }
}
