using System.Text;
using System.Xml.Linq;
using Logistica.Modules.Envios.Application.Features.ImprimirEtiquetas;
using Logistica.Modules.Envios.Infrastructure.Etiquetas;
using QuestPDF.Infrastructure;
using ZXing.Common;
using ZXing.OneD;

namespace Logistica.UnitTests.Envios;

public sealed class EtiquetasPdfTests
{
    [Theory]
    [InlineData("ENV-000001-1")]
    [InlineData("ENV-000001-12")]
    [InlineData("ENV-12345678901234567890-2")]
    public void Las_barras_vectoriales_se_decodifican_con_el_codigo_completo(string codigo)
    {
        var svg = XDocument.Parse(GeneradorEtiquetasPdf.CodigoBarras(codigo));
        var ns = svg.Root!.Name.Namespace;
        var ancho = int.Parse(svg.Root.Attribute("viewBox")!.Value.Split(' ')[2]);
        var fila = new BitArray(ancho);
        foreach (var rectangulo in svg.Root.Elements(ns + "rect").Where(e => e.Attribute("fill")?.Value == "black"))
            fila[int.Parse(rectangulo.Attribute("x")!.Value)] = true;
        var resultado = new Code128Reader().decodeRow(0, fila, null);
        Assert.NotNull(resultado);
        Assert.Equal(codigo, resultado.Text);
    }

    [Fact]
    public void Genera_pdf_con_doce_bultos_textos_largos_y_logo_svg()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var logo = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 120 40\"><rect width=\"120\" height=\"40\" fill=\"#164e63\"/><path d=\"M10 10h45v20H10z\" fill=\"white\"/></svg>");
        var etiquetas = Enumerable.Range(1, 12).Select(i => new EtiquetaBulto("ENV-000001", $"ENV-000001-{i}", i, 12,
            "María José Fernández de los Santos", "Avenida General José María Paz 12345, apartamento 402\nCiudad de la Costa, Canelones · CP 15000\nEntrada por calle lateral, junto al portón azul; consultar en recepción.",
            "Costa de Oro y Ciudad de la Costa", "Estándar")).ToArray();
        var generador = new GeneradorEtiquetasPdf();
        var pdf = generador.Generar(new("Operador Demo", "image/svg+xml", logo), etiquetas);
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        var sinLogo = generador.Generar(new("Envíos del Este", null, null), etiquetas.Take(1).ToArray());
        Assert.True(sinLogo.AsSpan().StartsWith("%PDF-"u8));
        // Exportación optativa para QA visual y lectura del PDF real con Poppler/pypdf, sin archivos en el repo.
        if (Environment.GetEnvironmentVariable("CU15_QA_DIR") is { Length: > 0 } ruta)
        {
            Directory.CreateDirectory(ruta);
            File.WriteAllBytes(Path.Combine(ruta, "etiquetas-doce-bultos.pdf"), pdf);
            File.WriteAllBytes(Path.Combine(ruta, "etiqueta-sin-logo.pdf"), sinLogo);
        }
    }

    [Fact]
    public void Un_codigo_largo_no_desborda_el_ancho_de_la_etiqueta()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var numero = "ENV-12345678901234567890123456789012";
        var pdf = new GeneradorEtiquetasPdf().Generar(new("Operador Demo", null, null),
            [new(numero, numero + "-12", 12, 12, "Ana", "Calle 1", "Centro", "Urgente")]);
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
    }

    [Theory]
    [InlineData("image/png", "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAI0lEQVR4nGMU80tmIAUwkaSaYVQDcYCJSHVwMKqBGEByKAEAk8MA59w9yaUAAAAASUVORK5CYII=")]
    [InlineData("image/jpeg", "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDz+iiivSPEP//Z")]
    public void Acepta_los_logos_raster_de_administracion(string tipo, string base64)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = new GeneradorEtiquetasPdf().Generar(new("Operador", tipo, Convert.FromBase64String(base64)),
            [new("ENV-000001", "ENV-000001-1", 1, 1, "Ana", "Calle 1", "Centro", "Estándar")]);
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
    }
}
