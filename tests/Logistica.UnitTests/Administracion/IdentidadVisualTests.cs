using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

// CU-07: las reglas de la identidad visual y del logo.
public class IdentidadVisualTests
{
    [Fact]
    public void Un_operador_nuevo_tiene_la_identidad_por_defecto_y_se_puede_cambiar()
    {
        var operador = Operador.Crear("Rápido SA", "rapido", "America/Montevideo");
        Assert.Equal(IdentidadVisual.PorDefecto, operador.Identidad);

        var nueva = new IdentidadVisual("#1a56db", "#93C5FD", null, "hola@rapido.uy", "099 000 000");
        operador.ActualizarIdentidad(nueva);

        Assert.Equal("#1A56DB", operador.Identidad.ColorPrimario);
    }

    // A2: el color tiene que ser hexadecimal.
    [Theory]
    [InlineData("azul")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void Un_color_que_no_es_hexadecimal_se_rechaza(string color)
    {
        Assert.Throws<DomainException>(() => new IdentidadVisual(color, "#FFFFFF", null, null, null));
    }

    [Fact]
    public void Un_correo_de_contacto_sin_arroba_se_rechaza()
    {
        Assert.Throws<DomainException>(() => new IdentidadVisual("#1A56DB", "#FFFFFF", null, "contacto", null));
    }

    // A3: es una advertencia, no un error: la identidad se crea igual.
    [Theory]
    [InlineData("#1A56DB", true)]
    [InlineData("#FFEB3B", false)]
    public void Avisa_si_el_texto_blanco_no_se_lee_sobre_el_color_primario(string primario, bool legible)
    {
        var identidad = new IdentidadVisual(primario, "#FFFFFF", null, null, null);

        Assert.Equal(legible, identidad.TextoBlancoLegible());
    }

    // A1: PNG, JPG o SVG, de hasta 1 MB.
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/svg+xml")]
    public void Se_aceptan_logos_png_jpg_y_svg(string tipo)
    {
        var logo = LogoOperador.Crear(Guid.NewGuid(), tipo, [1, 2, 3]);

        Assert.Equal(tipo, logo.TipoContenido);
    }

    [Fact]
    public void Se_rechaza_un_logo_de_otro_tipo_o_de_mas_de_1_MB()
    {
        Assert.Throws<DomainException>(() => LogoOperador.Crear(Guid.NewGuid(), "application/pdf", [1]));
        Assert.Throws<DomainException>(() =>
            LogoOperador.Crear(Guid.NewGuid(), "image/png", new byte[LogoOperador.MaximoBytes + 1]));
        Assert.Throws<DomainException>(() => LogoOperador.Crear(Guid.NewGuid(), "image/png", []));
    }
}
