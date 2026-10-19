using System.Globalization;
using Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Tarifas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Presentation.Features.ConfigurarTarifario;

// Traducciones entre las pantallas de CU-05 y lo que reciben los handlers.
internal static class TextosTarifario
{
    public static string Modalidad(Modalidad modalidad) => modalidad == Contracts.Modalidad.Urgente ? "Urgente" : "Estándar";

    public static string Ajuste(TipoAjuste tipo) => tipo == TipoAjuste.Recargo ? "Recargo" : "Bonificación";

    public static (string Texto, string Clase) Situacion(SituacionVersion situacion) => situacion switch
    {
        SituacionVersion.Vigente => ("Vigente", "text-bg-success"),
        SituacionVersion.Programada => ("Programada", "text-bg-info"),
        SituacionVersion.Borrador => ("Borrador", "text-bg-warning"),
        _ => ("Reemplazada", "text-bg-secondary"),
    };

    public static string Fecha(DateTimeOffset? fecha) => fecha?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—";

    public static string Numero(decimal valor) => valor.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Valor(AjusteDto ajuste) => ajuste.EsPorcentaje ? $"{Numero(ajuste.Valor)} %" : $"$ {Numero(ajuste.Valor)}";

    // Los números llegan como texto: el administrador puede escribir "0,05" o "0.05", sin depender del idioma del servidor.
    public static decimal Decimal(string? texto, string campo)
    {
        var normalizado = (texto ?? "").Trim().Replace(',', '.');
        return decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : throw new DomainException($"Ingresá un número válido en {campo}.");
    }
}
