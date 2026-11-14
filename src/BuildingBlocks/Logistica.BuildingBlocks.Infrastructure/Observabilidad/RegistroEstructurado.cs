using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Logistica.BuildingBlocks.Infrastructure.Observabilidad;

// Registro estructurado con Serilog (letra, sección 6.12), igual en la API y en el Worker. El código sigue usando
// ILogger<T>; Serilog es el que arma cada evento:
// - Consola en JSON compacto, con sus propiedades por separado (no un texto armado) y el TraceId/SpanId de la actividad
//   en curso: un log de la sincronización de la PWA, del outbox o del Worker se encuentra por la misma traza que se ve en
//   Aspire Dashboard (identificador de correlación).
// - Con OTEL_EXPORTER_OTLP_ENDPOINT configurado, los mismos eventos se mandan también por OTLP (Aspire Dashboard, en
//   Docker y en Azure), con el nombre del servicio que ya usan las trazas y métricas. En ese caso se quita el proveedor
//   de consola de .NET para no escribir cada línea dos veces.
// Los niveles se pueden cambiar sin recompilar con la sección "Serilog" de la configuración (Serilog:MinimumLevel).
public static class RegistroEstructurado
{
    public static IServiceCollection AddRegistroEstructurado(this IServiceCollection services, IConfiguration configuracion,
        string servicio)
    {
        var exportarOtlp = !string.IsNullOrWhiteSpace(configuracion["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (exportarOtlp)
        {
            services.AddLogging(proveedores => proveedores.ClearProviders());
            services.AddOpenTelemetry().WithLogging(logs => logs.AddOtlpExporter(), opciones =>
            {
                opciones.IncludeScopes = true;
                opciones.IncludeFormattedMessage = true;
            });
        }

        services.AddSerilog((_, registro) => registro
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Servicio", servicio)
                .WriteTo.Console(new RenderedCompactJsonFormatter())
                .ReadFrom.Configuration(configuracion),
            // Cada host arma su propio logger: las pruebas levantan varios en paralelo y no deben pisarse.
            preserveStaticLogger: true,
            // Serilog reenvía cada evento a los proveedores registrados en DI: acá, el de OpenTelemetry.
            writeToProviders: exportarOtlp);

        return services;
    }
}
