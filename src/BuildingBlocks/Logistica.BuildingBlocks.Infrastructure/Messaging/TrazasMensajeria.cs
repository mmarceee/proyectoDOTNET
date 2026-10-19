using System.Diagnostics;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging;

// Trazas del publicador y del consumidor (ADR-0003, sección 2.9). La API y el Worker registran esta fuente en
// OpenTelemetry; sin un oyente, StartActivity devuelve null y el mensaje conserva el traceparent del outbox.
public static class TrazasMensajeria
{
    public const string Fuente = "Logistica.Mensajeria";

    private static readonly ActivitySource Origen = new(Fuente);

    // Continúa la traza del request que guardó el evento. El id de este span viaja en la cabecera traceparent, así
    // el consumidor queda como hijo de la publicación.
    internal static Activity? IniciarPublicacion(string tipo, Guid messageId, string? traceParent)
    {
        var actividad = Iniciar($"Publicar {tipo}", ActivityKind.Producer, traceParent);
        actividad?.SetTag("messaging.system", "rabbitmq");
        actividad?.SetTag("messaging.operation.type", "publish");
        actividad?.SetTag("messaging.destination.name", RabbitMqTransport.Exchange);
        actividad?.SetTag("messaging.rabbitmq.destination.routing_key", tipo);
        actividad?.SetTag("messaging.message.id", messageId);
        return actividad;
    }

    internal static Activity? IniciarProcesamiento(string cola, MensajeIntegracion mensaje)
    {
        var actividad = Iniciar($"Procesar {cola}", ActivityKind.Consumer, mensaje.TraceParent);
        actividad?.SetTag("messaging.system", "rabbitmq");
        actividad?.SetTag("messaging.operation.type", "process");
        actividad?.SetTag("messaging.destination.name", cola);
        actividad?.SetTag("messaging.message.id", mensaje.Id);
        actividad?.SetTag("logistica.evento.tipo", mensaje.Tipo);
        actividad?.SetTag("logistica.operador.id", mensaje.OperadorId);
        return actividad;
    }

    // Sin traceparent válido (un mensaje viejo o publicado a mano), el span empieza una traza nueva.
    private static Activity? Iniciar(string nombre, ActivityKind tipo, string? traceParent)
        => ActivityContext.TryParse(traceParent, null, out var padre)
            ? Origen.StartActivity(nombre, tipo, padre)
            : Origen.StartActivity(nombre, tipo);
}
