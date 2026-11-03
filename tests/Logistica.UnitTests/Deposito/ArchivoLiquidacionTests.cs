using System.Text;
using Logistica.Modules.Deposito.Application.Features.ConsultarCuentaCorriente;
using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.Modules.Deposito.Presentation.Features.ConsultarCuentaCorriente;

namespace Logistica.UnitTests.Deposito;

// CU-33, paso 5: el CSV que descarga el comercio se abre en una planilla en español.
public class ArchivoLiquidacionTests
{
    [Fact]
    public void Lleva_BOM_punto_y_coma_coma_decimal_y_los_totales()
    {
        var ahora = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var liquidacion = Liquidacion.Generar(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31), [new DatosLinea(Guid.NewGuid(), "ENV-000007", ahora, "Envío devuelto", 1234.5m)], ahora);
        liquidacion.Emitir(ahora);
        var detalle = new DetalleLiquidacion(liquidacion,
            [new LineaCuentaCorriente("ENV-000007", new DateOnly(2026, 10, 28), "Envío devuelto", 1234.5m)]);

        var bytes = ConsultarCuentaCorrienteEndpoint.Csv(detalle);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lineas = Encoding.UTF8.GetString(bytes[3..]).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
        [
            "Liquidación;del 01/10/2026 al 31/10/2026;Emitida",
            "Envío;Fecha;Concepto;Monto",
            "ENV-000007;28/10/2026;Envío devuelto;1234,50",
            ";;Total bruto;1234,50",
            ";;Ajustes;0,00",
            ";;Total neto;1234,50",
        ], lineas);
    }
}
