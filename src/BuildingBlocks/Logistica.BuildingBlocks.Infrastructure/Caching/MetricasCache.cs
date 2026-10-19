using System.Diagnostics.Metrics;

namespace Logistica.BuildingBlocks.Infrastructure.Caching;

// Métricas de la caché (letra: "caché en funcionamiento con su métrica de aciertos"). La API registra este medidor
// en OpenTelemetry y Aspire Dashboard muestra los contadores, separados por la etiqueta "cache". La tasa de aciertos
// de una caché es aciertos / (aciertos + fallos).
public static class MetricasCache
{
    public const string Medidor = "Logistica.Cache";

    private static readonly Meter Origen = new(Medidor);

    private static readonly Counter<long> Aciertos = Origen.CreateCounter<long>(
        "logistica.cache.aciertos", "{lectura}", "Lecturas resueltas desde la caché.");

    private static readonly Counter<long> Fallos = Origen.CreateCounter<long>(
        "logistica.cache.fallos", "{lectura}", "Lecturas que no estaban en la caché y se calcularon desde la fuente.");

    private static readonly Counter<long> Errores = Origen.CreateCounter<long>(
        "logistica.cache.errores", "{operacion}", "Operaciones con la caché que fallaron; la lectura siguió desde la fuente.");

    internal static void Acierto(string cache) => Aciertos.Add(1, Etiqueta(cache));

    internal static void Fallo(string cache) => Fallos.Add(1, Etiqueta(cache));

    internal static void Error(string cache) => Errores.Add(1, Etiqueta(cache));

    private static KeyValuePair<string, object?> Etiqueta(string cache) => new("cache", cache);
}
