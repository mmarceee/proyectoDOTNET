using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Logistica.BuildingBlocks.Infrastructure.Caching;

// Métricas de la caché (letra: "caché en funcionamiento con su métrica de aciertos"). La API registra este medidor
// en OpenTelemetry y Aspire Dashboard muestra los contadores, separados por la etiqueta "cache". La relación de
// aciertos (aciertos / (aciertos + fallos), desde que arrancó el proceso) se publica además como medidor, porque
// el tablero no calcula cocientes entre dos métricas.
public static class MetricasCache
{
    public const string Medidor = "Logistica.Cache";

    private static readonly Meter Origen = new(Medidor);
    private static readonly ConcurrentDictionary<string, long> TotalAciertos = new();
    private static readonly ConcurrentDictionary<string, long> TotalFallos = new();

    private static readonly Counter<long> Aciertos = Origen.CreateCounter<long>(
        "logistica.cache.aciertos", "{lectura}", "Lecturas resueltas desde la caché.");

    private static readonly Counter<long> Fallos = Origen.CreateCounter<long>(
        "logistica.cache.fallos", "{lectura}", "Lecturas que no estaban en la caché y se calcularon desde la fuente.");

    private static readonly Counter<long> Errores = Origen.CreateCounter<long>(
        "logistica.cache.errores", "{operacion}", "Operaciones con la caché que fallaron; la lectura siguió desde la fuente.");

    private static readonly ObservableGauge<double> RelacionAciertos = Origen.CreateObservableGauge(
        "logistica.cache.relacion_aciertos", MedirRelacion, "%",
        "Porcentaje de lecturas resueltas desde la caché desde que arrancó el proceso.");

    internal static void Acierto(string cache)
    {
        Aciertos.Add(1, Etiqueta(cache));
        TotalAciertos.AddOrUpdate(cache, 1, (_, total) => total + 1);
    }

    internal static void Fallo(string cache)
    {
        Fallos.Add(1, Etiqueta(cache));
        TotalFallos.AddOrUpdate(cache, 1, (_, total) => total + 1);
    }

    internal static void Error(string cache) => Errores.Add(1, Etiqueta(cache));

    private static IEnumerable<Measurement<double>> MedirRelacion()
    {
        foreach (var cache in TotalAciertos.Keys.Union(TotalFallos.Keys))
        {
            var aciertos = TotalAciertos.GetValueOrDefault(cache);
            var lecturas = aciertos + TotalFallos.GetValueOrDefault(cache);
            if (lecturas > 0)
            {
                yield return new Measurement<double>(100.0 * aciertos / lecturas, Etiqueta(cache));
            }
        }
    }

    private static KeyValuePair<string, object?> Etiqueta(string cache) => new("cache", cache);
}
