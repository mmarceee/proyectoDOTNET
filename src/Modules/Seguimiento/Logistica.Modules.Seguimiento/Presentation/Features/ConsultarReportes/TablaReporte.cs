using System.Globalization;
using System.Text;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarReportes;

namespace Logistica.Modules.Seguimiento.Presentation.Features.ConsultarReportes;

// CU-72, pasos 3 y 4: el reporte como tabla de texto, la misma para la página y para el CSV. Números con coma decimal,
// como el CSV de la cuenta corriente (CU-33), para abrirlo en una planilla en español.
internal sealed record TablaReporte(string Titulo, IReadOnlyList<string> Columnas, IReadOnlyList<bool> Numericas,
    IReadOnlyList<IReadOnlyList<string>> Filas)
{
    private static readonly NumberFormatInfo Numeros = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };

    public static TablaReporte Desde(Reporte reporte) => reporte.Query.Tipo switch
    {
        TipoReporte.Motivos => new("Motivos de no entrega", ["Motivo", "Intentos fallidos", "% del total"],
            [false, true, true],
            reporte.Motivos.Select(m => Fila(m.Motivo, Entero(m.Cantidad), Decimal(m.Porcentaje))).ToList()),
        TipoReporte.Volumen => new("Volumen de envíos por día", ["Día", "Altas", "Entregas", "Intentos fallidos"],
            [false, true, true, true],
            reporte.Volumen.Select(v => Fila(Dia(v.Dia), Entero(v.Altas), Entero(v.Entregas), Entero(v.IntentosFallidos)))
                .ToList()),
        TipoReporte.Liquidacion => new("Liquidación por comercio",
            ["Comercio", "Liquidaciones", "Envíos", "Total neto", "Cobrado", "Pendiente"],
            [false, true, true, true, true, true],
            reporte.Liquidacion.Select(l => Fila(l.Comercio, Entero(l.Liquidaciones), Entero(l.Envios), Monto(l.TotalNeto),
                Monto(l.Cobrado), Monto(l.Pendiente))).ToList()),
        _ => new($"Cumplimiento por {Dimension(reporte.Query.Dimension).ToLowerInvariant()}",
            [Dimension(reporte.Query.Dimension), "Entregas", "Con ventana", "A tiempo", "% a tiempo", "Intentos fallidos"],
            [false, true, true, true, true, true],
            reporte.Cumplimiento.Select(c => Fila(c.Nombre, Entero(c.Entregas), Entero(c.ConVentana), Entero(c.ATiempo),
                c.PorcentajeATiempo is decimal p ? Decimal(p) : "—", Entero(c.IntentosFallidos))).ToList()),
    };

    public static string Dimension(DimensionCumplimiento dimension) => dimension switch
    {
        DimensionCumplimiento.Repartidor => "Repartidor",
        DimensionCumplimiento.Comercio => "Comercio",
        _ => "Zona",
    };

    // Con BOM, para que la planilla reconozca el UTF-8 de los acentos. El punto y coma separa porque la coma es decimal.
    public byte[] Csv(DateOnly desde, DateOnly hasta)
    {
        var texto = new StringBuilder();
        texto.AppendLine($"{Celda(Titulo)};del {Dia(desde)} al {Dia(hasta)}");
        texto.AppendLine(string.Join(';', Columnas.Select(Celda)));
        foreach (var fila in Filas)
            texto.AppendLine(string.Join(';', fila.Select(Celda)));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texto.ToString())];
    }

    private static IReadOnlyList<string> Fila(params string[] celdas) => celdas;

    // Un nombre con punto y coma, comillas o saltos de línea va entre comillas, con las comillas duplicadas.
    private static string Celda(string valor) =>
        valor.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;

    private static string Entero(int valor) => valor.ToString(CultureInfo.InvariantCulture);

    private static string Decimal(decimal valor) => valor.ToString("0.0", Numeros);

    private static string Monto(decimal valor) => valor.ToString("0.00", Numeros);

    // Con la cultura invariante, la "/" del formato no depende del idioma del servidor.
    private static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
