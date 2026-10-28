using Logistica.BuildingBlocks.Infrastructure.Caching;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento;
using Logistica.SharedKernel;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Trazas (ADR-0003, sección 2.9) y métricas de la caché (ADR-0006, sección 2.7): cada mensaje continúa la traza de su
// publicación en la API, y las invalidaciones del seguimiento cuentan en el mismo tablero que las lecturas de la API.
// Se exportan a Aspire Dashboard cuando OTEL_EXPORTER_OTLP_ENDPOINT está configurado, igual que en la API.
var exportarOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(recurso => recurso.AddService("logistica-worker"))
    .WithTracing(trazas =>
    {
        trazas.AddNpgsql()
            .AddSource(TrazasMensajeria.Fuente);

        if (exportarOtlp)
        {
            trazas.AddOtlpExporter();
        }
    })
    .WithMetrics(metricas =>
    {
        metricas.AddMeter(MetricasCache.Medidor);

        if (exportarOtlp)
        {
            metricas.AddOtlpExporter();
        }
    });

builder.Services.AddScoped<InquilinoMensaje>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<InquilinoMensaje>());
builder.Services.AddSeguimientoModule(builder.Configuration);
// CU-60: el Worker borra el seguimiento de la caché ante cada cambio de estado (InvalidarSeguimiento).
builder.Services.AddCacheDistribuida(builder.Configuration.GetConnectionString("Valkey"));
builder.Services.AddHostedService<ConsumidorRabbitMq>();

var host = builder.Build();
host.Run();
