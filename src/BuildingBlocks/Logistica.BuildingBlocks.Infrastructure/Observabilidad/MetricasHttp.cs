using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Logistica.BuildingBlocks.Infrastructure.Observabilidad;

// Métricas de las solicitudes HTTP para el tablero técnico (letra, sección 6.12). Aspire Dashboard dibuja los
// histogramas con los percentiles 50, 90 y 99, sin el 95 ni el tiempo medio; por eso la API los calcula sobre los
// últimos cinco minutos (VentanaLatencias) y los publica como medidores. Un error es una respuesta 5xx o una excepción.
public static class MetricasHttp
{
    public const string Medidor = "Logistica.Http";

    private static readonly Meter Origen = new(Medidor);
    private static readonly VentanaLatencias Ventana = new(TimeProvider.System);

    private static readonly ObservableGauge<double> Media = Origen.CreateObservableGauge(
        "logistica.http.latencia.media", () => Medir(r => r.MediaMs), "ms",
        "Tiempo medio de respuesta de los últimos cinco minutos.");

    private static readonly ObservableGauge<double> P95 = Origen.CreateObservableGauge(
        "logistica.http.latencia.p95", () => Medir(r => r.P95Ms), "ms",
        "Percentil 95 de la latencia de los últimos cinco minutos.");

    private static readonly ObservableGauge<double> P99 = Origen.CreateObservableGauge(
        "logistica.http.latencia.p99", () => Medir(r => r.P99Ms), "ms",
        "Percentil 99 de la latencia de los últimos cinco minutos.");

    private static readonly ObservableGauge<double> TasaErrores = Origen.CreateObservableGauge(
        "logistica.http.tasa_errores", () => Medir(r => r.PorcentajeErrores), "%",
        "Porcentaje de respuestas 5xx o con excepción de los últimos cinco minutos.");

    private static readonly ObservableGauge<int> Solicitudes = Origen.CreateObservableGauge(
        "logistica.http.solicitudes", () => Ventana.Resumir().Solicitudes, "{solicitud}",
        "Solicitudes de los últimos cinco minutos.");

    // Mide cada solicitud salvo /health y las conexiones de SignalR (/hubs), que duran lo que dura la sesión y
    // deformarían el tiempo medio. Va antes del manejador de excepciones, para ver el código de respuesta final.
    public static IApplicationBuilder UseMetricasHttp(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (http.Request.Path.StartsWithSegments("/health") || http.Request.Path.StartsWithSegments("/hubs"))
        {
            await next(http);
            return;
        }

        var inicio = Stopwatch.GetTimestamp();
        var error = true;
        try
        {
            await next(http);
            error = http.Response.StatusCode >= StatusCodes.Status500InternalServerError;
        }
        finally
        {
            Ventana.Registrar(Stopwatch.GetElapsedTime(inicio).TotalMilliseconds, error);
        }
    });

    // Sin solicitudes en la ventana no se informa ningún valor: un cero parecería una respuesta instantánea.
    private static IEnumerable<Measurement<double>> Medir(Func<ResumenLatencias, double> valor)
    {
        var resumen = Ventana.Resumir();
        return resumen.Solicitudes == 0 ? [] : [new Measurement<double>(valor(resumen))];
    }
}
