using Logistica.Modules.Seguimiento.Domain.Tablero;

namespace Logistica.UnitTests.Seguimiento;

// CU-66, requerimiento especial 3: envío en riesgo = no entregado y con menos de 60 minutos hasta el fin de su ventana.
public class RiesgoDeEntregaTests
{
    private static readonly TimeZoneInfo Montevideo = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");
    private static readonly DateOnly Fecha = new(2026, 10, 27);
    private static readonly TimeOnly Hasta = new(18, 0);

    // La franja termina a las 18:00 de Montevideo (UTC-3): las 21:00 UTC.
    private static readonly DateTimeOffset FinUtc = new(2026, 10, 27, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void El_fin_de_la_ventana_es_el_final_de_la_franja_en_la_zona_del_operador()
    {
        Assert.Equal(FinUtc, RiesgoDeEntrega.FinDeVentana(Fecha, Hasta, Montevideo));
    }

    [Theory]
    [InlineData(120, false)] // 16:00 en Montevideo: quedan dos horas
    [InlineData(60, false)]  // justo el umbral: "menos de 60 minutos" todavía no
    [InlineData(59, true)]
    [InlineData(1, true)]
    public void Esta_en_riesgo_con_menos_de_60_minutos(int minutosRestantes, bool enRiesgo)
    {
        var evaluacion = RiesgoDeEntrega.Evaluar(Fecha, Hasta, Montevideo, FinUtc.AddMinutes(-minutosRestantes));

        Assert.Equal(enRiesgo, evaluacion.EnRiesgo);
        Assert.Equal(TimeSpan.FromMinutes(minutosRestantes), evaluacion.Restante);
        Assert.False(evaluacion.Vencida);
    }

    // La ventana ya terminó sin entrega: es el caso más urgente, sigue en riesgo y queda marcada como vencida.
    [Fact]
    public void Una_ventana_vencida_esta_en_riesgo_y_vencida()
    {
        var evaluacion = RiesgoDeEntrega.Evaluar(Fecha, Hasta, Montevideo, FinUtc.AddMinutes(15));

        Assert.True(evaluacion.EnRiesgo);
        Assert.True(evaluacion.Vencida);
    }

    // El día anterior a la fecha programada no está en riesgo, aunque la hora sea la misma.
    [Fact]
    public void Un_envio_programado_para_otro_dia_no_esta_en_riesgo()
    {
        var evaluacion = RiesgoDeEntrega.Evaluar(Fecha, Hasta, Montevideo, FinUtc.AddDays(-1).AddMinutes(-30));

        Assert.False(evaluacion.EnRiesgo);
    }
}
