using System.Text;
using Logistica.Modules.Envios.Application.Features.ImprimirEtiquetas;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.OneD;

namespace Logistica.Modules.Envios.Infrastructure.Etiquetas;

internal sealed class GeneradorEtiquetasPdf : IGeneradorEtiquetas
{
    public byte[] Generar(MarcaEtiquetas marca, IReadOnlyList<EtiquetaBulto> etiquetas) => Document.Create(document =>
    {
        foreach (var etiqueta in etiquetas)
            document.Page(page =>
            {
                page.Size(100, 150, Unit.Millimetre);
                page.Margin(5, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Lato").FontSize(10));
                page.Content().Column(column =>
                {
                    column.Spacing(8);
                    column.Item().Height(34).Row(row =>
                    {
                        if (marca.Logo is { Length: > 0 })
                        {
                            var imagen = row.ConstantItem(65).PaddingRight(8);
                            if (marca.TipoLogo == "image/svg+xml") imagen.Svg(Encoding.UTF8.GetString(marca.Logo)).FitArea();
                            else imagen.Image(marca.Logo).FitArea();
                        }
                        row.RelativeItem().AlignMiddle().ScaleToFit().Text(marca.Nombre).Bold().FontSize(13);
                    });
                    column.Item().LineHorizontal(1);
                    column.Item().Text(etiqueta.NumeroEnvio).Bold().FontSize(15);
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Bulto {etiqueta.Posicion} de {etiqueta.Total}").Bold().FontSize(13);
                        row.AutoItem().Text(etiqueta.Modalidad).Bold();
                    });
                    // Área independiente: el texto largo puede encogerse sin reducir las barras.
                    column.Item().Height(170).ScaleToFit().Column(datos =>
                    {
                        datos.Spacing(6);
                        datos.Item().Text("DESTINATARIO").FontSize(8).FontColor(Colors.Grey.Darken2);
                        datos.Item().Text(etiqueta.Destinatario).Bold().FontSize(14);
                        datos.Item().Text(etiqueta.Direccion).FontSize(12);
                        datos.Item().Text($"Zona: {etiqueta.Zona}").Bold();
                    });
                    column.Item().Height(62).AlignCenter().Svg(CodigoBarras(etiqueta.Codigo)).FitArea();
                    column.Item().AlignCenter().Text(etiqueta.Codigo).FontSize(9);
                });
            });
    }).GeneratePdf();

    // SVG vectorial, con diez módulos blancos a cada lado; no depende de System.Drawing ni de servicios externos.
    internal static string CodigoBarras(string codigo)
    {
        var barras = new Code128Writer().encode(codigo);
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {barras.Length + 20} 60\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        for (var i = 0; i < barras.Length; i++)
            if (barras[i]) svg.Append($"<rect x=\"{i + 10}\" y=\"0\" width=\"1\" height=\"60\" fill=\"black\"/>");
        return svg.Append("</svg>").ToString();
    }
}
