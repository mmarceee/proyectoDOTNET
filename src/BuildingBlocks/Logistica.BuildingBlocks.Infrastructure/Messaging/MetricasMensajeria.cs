using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging;

// Profundidad de las colas para el tablero técnico (letra, sección 6.12). RabbitMQ informa en cada BasicGet cuántos
// mensajes quedan en la cola; el consumidor guarda ese valor y el medidor lo publica, con la cola como etiqueta. Las
// colas de espera de los reintentos no se leen, así que no aparecen.
public static class MetricasMensajeria
{
    public const string Medidor = "Logistica.Mensajeria";

    private static readonly Meter Origen = new(Medidor);
    private static readonly ConcurrentDictionary<string, long> Profundidades = new();

    private static readonly ObservableGauge<long> Profundidad = Origen.CreateObservableGauge(
        "logistica.mensajeria.profundidad_cola",
        () => Profundidades.Select(p => new Measurement<long>(p.Value, new KeyValuePair<string, object?>("cola", p.Key))),
        "{mensaje}",
        "Mensajes que esperan en cada cola, según la última lectura del consumidor.");

    private static readonly ObservableGauge<long> ProfundidadTotal = Origen.CreateObservableGauge(
        "logistica.mensajeria.profundidad_total", () => Profundidades.Values.Sum(), "{mensaje}",
        "Mensajes que esperan en todas las colas leídas por este proceso.");

    // restantes: lo que informa BasicGet (los mensajes que quedan sin contar el entregado), o 0 si la cola estaba vacía.
    internal static void Leida(string cola, uint restantes) => Profundidades[cola] = restantes;
}
