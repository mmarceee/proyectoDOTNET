using System.Text;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarReportes;
using Logistica.Modules.Seguimiento.Presentation.Features.ConsultarReportes;

namespace Logistica.UnitTests.Seguimiento;

// CU-72, pasos 3 y 4: la tabla del reporte y su CSV.
public class TablaReporteTests
{
    private static readonly DateOnly Desde = new(2026, 11, 1);
    private static readonly DateOnly Hasta = new(2026, 11, 6);

    private static Reporte Vacio(TipoReporte tipo, DimensionCumplimiento dimension = DimensionCumplimiento.Zona) =>
        new(new ConsultarReporteQuery(tipo, Desde, Hasta, dimension), [], [], [], []);

    [Fact]
    public void El_cumplimiento_sin_ventana_muestra_un_guion_en_el_porcentaje()
    {
        var reporte = Vacio(TipoReporte.Cumplimiento, DimensionCumplimiento.Repartidor) with
        {
            Cumplimiento = [new FilaCumplimiento("Ana", 3, 3, 2, 1), new FilaCumplimiento("Beto", 1, 0, 0, 0)],
        };

        var tabla = TablaReporte.Desde(reporte);

        Assert.Equal("Cumplimiento por repartidor", tabla.Titulo);
        Assert.Equal("Repartidor", tabla.Columnas[0]);
        Assert.Equal(new[] { "Ana", "3", "3", "2", "66,7", "1" }, tabla.Filas[0]);
        Assert.Equal("—", tabla.Filas[1][4]);
    }

    [Fact]
    public void El_csv_usa_punto_y_coma_coma_decimal_bom_y_escapa_los_nombres()
    {
        var reporte = Vacio(TipoReporte.Liquidacion) with
        {
            Liquidacion = [new FilaLiquidacion("Tienda \"La Esquina\"; SA", 2, 15, 1234.5m, 1000m, 234.5m)],
        };

        var bytes = TablaReporte.Desde(reporte).Csv(Desde, Hasta);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lineas = Encoding.UTF8.GetString(bytes[3..]).Split(Environment.NewLine);
        Assert.Equal("Liquidación por comercio;del 01/11/2026 al 06/11/2026", lineas[0]);
        Assert.Equal("Comercio;Liquidaciones;Envíos;Total neto;Cobrado;Pendiente", lineas[1]);
        Assert.Equal("\"Tienda \"\"La Esquina\"\"; SA\";2;15;1234,50;1000,00;234,50", lineas[2]);
    }
}
