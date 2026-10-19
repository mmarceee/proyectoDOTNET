using Logistica.BuildingBlocks.Infrastructure.Caching;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento;
using Logistica.SharedKernel;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Trazas (ADR-0003, sección 2.9): cada mensaje continúa la traza de su publicación en la API, con sus consultas a
// PostgreSQL. Se exportan a Aspire Dashboard cuando OTEL_EXPORTER_OTLP_ENDPOINT está configurado, igual que en la API.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(recurso => recurso.AddService("logistica-worker"))
    .WithTracing(trazas =>
    {
        trazas.AddNpgsql()
            .AddSource(TrazasMensajeria.Fuente);

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            trazas.AddOtlpExporter();
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
