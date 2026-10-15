using System.Globalization;
using System.Text.RegularExpressions;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Operadores;

// La marca del operador (CU-07; RF 5). Sin id propio: es parte de Operador (modelo de dominio) y se reemplaza entera.
internal sealed partial record IdentidadVisual
{
    // La que tiene un operador recién creado, hasta que el administrador la configura.
    public static readonly IdentidadVisual PorDefecto = new("#0D6EFD", "#6C757D", null, null, null);

    public string ColorPrimario { get; }
    public string ColorSecundario { get; }

    // El archivo del logo, en la tabla de logos (propuesta de stack, sección 3.9). null: sin logo.
    public Guid? LogoArchivoId { get; }

    public string? EmailContacto { get; }
    public string? TelefonoContacto { get; }

    public IdentidadVisual(
        string colorPrimario, string colorSecundario, Guid? logoArchivoId, string? emailContacto, string? telefonoContacto)
    {
        ColorPrimario = Color(colorPrimario, "primario");
        ColorSecundario = Color(colorSecundario, "secundario");
        LogoArchivoId = logoArchivoId;
        EmailContacto = Opcional(emailContacto);
        TelefonoContacto = Opcional(telefonoContacto);

        if (EmailContacto is not null && !EmailContacto.Contains('@'))
        {
            throw new DomainException("El correo de contacto no es válido.");
        }
    }

    // CU-07, A3: ¿se lee el texto blanco sobre el color primario? Relación de contraste de WCAG, mínimo 4,5 a 1.
    // Es una advertencia: el administrador puede guardar igual.
    public bool TextoBlancoLegible()
    {
        var luminancia = Luminancia(ColorPrimario);
        return (1.0 + 0.05) / (luminancia + 0.05) >= 4.5;
    }

    // CU-07, A2: el color tiene que ser hexadecimal (#RRGGBB).
    private static string Color(string valor, string cual)
    {
        if (string.IsNullOrWhiteSpace(valor) || !ColorHexadecimal().IsMatch(valor.Trim()))
        {
            throw new DomainException($"El color {cual} tiene que ser hexadecimal, por ejemplo #1A56DB.");
        }

        return valor.Trim().ToUpperInvariant();
    }

    private static string? Opcional(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    private static double Luminancia(string color)
    {
        double Canal(int desde)
        {
            var c = int.Parse(color.AsSpan(desde, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Canal(1) + 0.7152 * Canal(3) + 0.0722 * Canal(5);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorHexadecimal();
}
