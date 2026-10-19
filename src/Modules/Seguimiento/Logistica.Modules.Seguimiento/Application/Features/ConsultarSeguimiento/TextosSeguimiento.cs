using System.Globalization;
using Logistica.Modules.Administracion.Contracts.Results;

namespace Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;

// Lo que ve la destinataria, en sus palabras (CU-60, paso 3). Son reglas de presentación del seguimiento, sin datos.
internal static class TextosSeguimiento
{
    private static readonly CultureInfo Uruguay = CultureInfo.GetCultureInfo("es-UY");

    // Los estados en que el envío ya no espera una entrega: no hay ventana que mostrar.
    private static readonly string[] Finales = ["Entregado", "EnDevolucion", "Devuelto", "Extraviado", "Cancelado"];

    // CU-61: el destinatario puede pedir otra fecha mientras el envío no salió a reparto (ficha de CU-61, precondición).
    private static readonly string[] Reprogramables = ["Admitido", "EnDeposito", "NoEntregado"];

    public static string Estado(string estado) => estado switch
    {
        "Admitido" => "Recibimos tu envío",
        "EnDeposito" => "En el depósito",
        "AsignadoARuta" => "Preparando la entrega",
        "EnTransito" => "En camino",
        "Entregado" => "Entregado",
        "NoEntregado" => "No pudimos entregarlo",
        "Reprogramado" => "Entrega reprogramada",
        "EnDevolucion" => "En devolución",
        "Devuelto" => "Devuelto al comercio",
        "Extraviado" => "Extraviado",
        "Cancelado" => "Cancelado",
        _ => estado,
    };

    public static bool PuedeReprogramar(string estado) => Reprogramables.Contains(estado);

    // Requerimiento 5 de la ficha, sin estimar tiempos de viaje: con fecha y franja asignadas, esa fecha y franja. Sin
    // fecha (todavía no se sabe la modalidad del envío para calcular el plazo) o con el envío terminado, no hay ventana.
    public static string? Ventana(string estado, DateOnly? fecha, FranjaPlanificacion? franja)
    {
        if (Finales.Contains(estado) || fecha is not DateOnly dia)
        {
            return null;
        }

        var texto = $"El {dia.ToString("dddd d 'de' MMMM", Uruguay)}";
        return franja is null ? texto : $"{texto}, entre las {franja.Desde:HH\\:mm} y las {franja.Hasta:HH\\:mm}";
    }
}
