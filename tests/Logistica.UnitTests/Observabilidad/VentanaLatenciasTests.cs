using Logistica.BuildingBlocks.Infrastructure.Observabilidad;

namespace Logistica.UnitTests.Observabilidad;

// Tiempo medio, percentiles y tasa de errores del tablero técnico (letra, sección 6.12).
public class VentanaLatenciasTests
{
    [Fact]
    public void Sin_solicitudes_el_resumen_queda_vacio()
    {
        var ventana = new VentanaLatencias(new Reloj());

        Assert.Equal(ResumenLatencias.SinSolicitudes, ventana.Resumir());
    }

    [Fact]
    public void Calcula_media_percentiles_y_tasa_de_errores()
    {
        var ventana = new VentanaLatencias(new Reloj());
        // 1, 2, …, 100 ms; las cinco más lentas fallaron.
        for (var ms = 1; ms <= 100; ms++)
        {
            ventana.Registrar(ms, error: ms > 95);
        }

        var resumen = ventana.Resumir();

        Assert.Equal(100, resumen.Solicitudes);
        Assert.Equal(50.5, resumen.MediaMs);
        Assert.Equal(95, resumen.P95Ms);
        Assert.Equal(99, resumen.P99Ms);
        Assert.Equal(5, resumen.PorcentajeErrores);
    }

    [Fact]
    public void Con_pocas_muestras_el_percentil_es_la_mas_lenta()
    {
        var ventana = new VentanaLatencias(new Reloj());
        ventana.Registrar(10, error: false);
        ventana.Registrar(300, error: false);

        var resumen = ventana.Resumir();

        Assert.Equal(300, resumen.P95Ms);
        Assert.Equal(300, resumen.P99Ms);
    }

    [Fact]
    public void Descarta_las_solicitudes_de_mas_de_cinco_minutos()
    {
        var reloj = new Reloj();
        var ventana = new VentanaLatencias(reloj);
        ventana.Registrar(1000, error: true);
        reloj.Avanzar(VentanaLatencias.Duracion + TimeSpan.FromSeconds(1));
        ventana.Registrar(20, error: false);

        var resumen = ventana.Resumir();

        Assert.Equal(1, resumen.Solicitudes);
        Assert.Equal(20, resumen.MediaMs);
        Assert.Equal(0, resumen.PorcentajeErrores);
    }

    [Fact]
    public void Conserva_como_mucho_el_maximo_de_muestras()
    {
        var ventana = new VentanaLatencias(new Reloj());
        for (var i = 0; i < VentanaLatencias.MaximoMuestras + 50; i++)
        {
            ventana.Registrar(1, error: false);
        }

        Assert.Equal(VentanaLatencias.MaximoMuestras, ventana.Resumir().Solicitudes);
    }

    private sealed class Reloj : TimeProvider
    {
        private DateTimeOffset ahora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => ahora;

        public void Avanzar(TimeSpan lapso) => ahora += lapso;
    }
}
