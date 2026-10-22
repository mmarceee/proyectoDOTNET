using System.Globalization;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;

namespace Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;

internal sealed record TextoNotificacion(string Asunto, string Titulo, string Texto);

// CU-62, paso 4: qué dice el correo de cada hecho relevante. El enlace de seguimiento va en todos.
internal static class TextosNotificacion
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-UY");

    public static TextoNotificacion Para(HechoEnvio hecho, string numero, string operador) => hecho.EstadoEnvio switch
    {
        "Admitido" => new($"Recibimos tu envío {numero}", "Tu envío fue registrado",
            $"{operador} registró el envío {numero} a tu nombre. Con el enlace de abajo podés seguirlo en todo momento."),
        "EnTransito" => new($"Tu envío {numero} está en camino", "Tu envío está en camino",
            $"El envío {numero} salió a reparto y hoy lo vamos a llevar a la dirección indicada."),
        "Entregado" => new($"Entregamos tu envío {numero}", "Tu envío fue entregado",
            $"El envío {numero} se entregó. ¡Gracias por elegir {operador}!"),
        "EnDevolucion" => new($"No pudimos entregar tu envío {numero}", "Tu envío vuelve al remitente",
            $"No pudimos entregar el envío {numero} y vuelve al comercio que lo envió. Por cualquier consulta, comunicate con el comercio."),
        "Reprogramado" when hecho.IntentoFallido => new($"No pudimos entregar tu envío {numero}", "Vamos a intentarlo de nuevo",
            $"Pasamos a entregar el envío {numero} pero no pudimos hacerlo. Vamos a intentarlo de nuevo y te avisamos la nueva fecha."),
        "Reprogramado" => new($"Nueva fecha de entrega para tu envío {numero}", "Tu entrega tiene una nueva fecha",
            hecho.NuevaFecha is DateOnly fecha
                ? $"El envío {numero} se va a entregar el {fecha.ToString("dddd d 'de' MMMM", Cultura)}."
                : $"El envío {numero} tiene una nueva fecha de entrega."),
        _ => throw new InvalidOperationException($"El estado {hecho.EstadoEnvio} no se notifica al destinatario."),
    };
}
