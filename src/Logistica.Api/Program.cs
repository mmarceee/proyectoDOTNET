using Logistica.Api.Errores;
using Logistica.Api.Tenancy;
using Logistica.Api.Seguridad;
using Logistica.BuildingBlocks.Infrastructure.Caching;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;
using Logistica.Modules.Administracion;
using Logistica.Modules.Deposito;
using Logistica.Modules.Ejecucion;
using Logistica.Modules.Envios;
using Logistica.Modules.Planificacion;
using System.Threading.RateLimiting;
using Logistica.Modules.Seguimiento;
using Logistica.Modules.Seguimiento.Contracts;
using Logistica.SharedKernel;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// Trazas distribuidas (letra, sección 6.12): cada request, sus consultas a PostgreSQL y las fuentes propias de los
// módulos, como la sincronización de la PWA (ADR-0004, sección 2.9), y la publicación del outbox, que el Worker
// continúa (ADR-0003, sección 2.9). Métricas, por ahora sólo las de la caché (aciertos, fallos y errores); el resto y
// los logs llegan con el hito del 5/11. Se exportan por OTLP a Aspire Dashboard cuando OTEL_EXPORTER_OTLP_ENDPOINT
// está configurado (docker-compose.yml).
var exportarOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(recurso => recurso.AddService("logistica-api"))
    .WithTracing(trazas =>
    {
        trazas.AddAspNetCoreInstrumentation()
            .AddNpgsql()
            .AddSource(EjecucionModule.FuenteDeTrazas, TrazasMensajeria.Fuente);

        if (exportarOtlp)
        {
            trazas.AddOtlpExporter();
        }
    })
    .WithMetrics(metricas =>
    {
        metricas.AddMeter(MetricasCache.Medidor);
        metricas.AddMeter(EjecucionModule.FuenteDeTrazas);

        if (exportarOtlp)
        {
            metricas.AddOtlpExporter();
        }
    });

// Caché distribuida: Valkey si hay cadena de conexión; si no, la memoria del proceso (CachingExtensions).
builder.Services.AddCacheDistribuida(builder.Configuration.GetConnectionString("Valkey"));

// Errores como ProblemDetails; las reglas de negocio violadas responden 400. Una escritura sobre datos
// de otro inquilino responde 404, igual que un recurso inexistente (ADR-0002, sección 2.9).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TenantMismatchExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrenciaExceptionHandler>();

// Un body sin un campo obligatorio, o con null donde el DTO no lo admite, responde 400 antes de llegar al endpoint.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});

// Reloj del sistema: los handlers lo reciben inyectado para que las pruebas puedan fijar la fecha.
builder.Services.AddSingleton(TimeProvider.System);

// Inquilino de cada request: de la cookie de sesión (ADR-0002, sección 2.5). TenantProvisorio queda sólo como
// respaldo de Development mientras las pantallas y las pruebas pasan a iniciar sesión.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantProvisorio>();
builder.Services.AddScoped<InquilinoMensaje>();
builder.Services.AddScoped<InquilinoTrabajo>();
builder.Services.AddScoped<TenantDeSesion>();
builder.Services.AddScoped<ICurrentTenant>(sp =>
{
    var mensaje = sp.GetRequiredService<InquilinoMensaje>();
    if (mensaje.Activo) return mensaje;
    var trabajo = sp.GetRequiredService<InquilinoTrabajo>();
    return trabajo.OperadorId is not null ? trabajo : sp.GetRequiredService<TenantDeSesion>();
});
builder.Services.AddAuthorization();

// Backoffice: las páginas viven en Logistica.Backoffice y en Presentation/Features de cada módulo.
// La raíz "/" permite descubrirlas fuera de la carpeta Pages; cada página declara su ruta absoluta
// con @page "/backoffice/..." (ADR-0001, addendum 4).
builder.Services.AddRazorPages(options => options.RootDirectory = "/");

builder.Services
    .AddAdministracionModule(builder.Configuration)
    .AddEnviosModule(builder.Configuration)
    .AddPlanificacionModule(builder.Configuration)
    .AddEjecucionModule(builder.Configuration)
    .AddSeguimientoModule(builder.Configuration)
    .AddDepositoModule(builder.Configuration)
    .AddMensajesFallidos(builder.Configuration.GetConnectionString("Postgres"));

if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("RabbitMQ")))
{
    builder.Services.AddHostedService<PublicadorOutbox>();
    builder.Services.AddHostedService<RegistradorFallidos>();
}

// CU-60, A3 (letra 6.3): el seguimiento público no tiene sesión, así que se limita por dirección IP. Frena a quien prueba
// enlaces al azar y protege el componente de más tráfico. Superado el límite, responde 429.
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.AddPolicy(LimitesSeguimiento.Politica, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Seguimiento:ConsultasPorMinuto", 60),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

// En Docker Compose la API aplica las migraciones de todos los módulos al iniciar (docker-compose.yml),
// y después carga los datos iniciales, que necesitan las tablas ya creadas.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateModuleDatabasesAsync();
    await app.Services.MigrarIdentidadAsync();
    await app.Services.SembrarDatosInicialesAsync();
}

// En Development las Minimal APIs lanzan BadHttpRequestException ante un body inválido, en lugar de
// responder 400. Se respeta su código para que no termine en un 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    // El arranque WASM referencia archivos con huellas que cambian al compilar. En desarrollo no conservar
    // respuestas de otra compilación; la política de publicación y el almacenamiento offline no se modifican.
    app.Use(async (http, next) =>
    {
        if (http.Request.Path.StartsWithSegments("/portal")
            || http.Request.Path.StartsWithSegments("/seguimiento")
            || http.Request.Path.StartsWithSegments("/repartidor"))
        {
            http.Response.OnStarting(() =>
            {
                http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                http.Response.Headers.Pragma = "no-cache";
                http.Response.Headers.Expires = "0";
                return Task.CompletedTask;
            });
        }
        await next();
    });
}

// Lee la cookie de sesión en cada request y arma el usuario, antes de que llegue a cualquier endpoint o página.
app.UseAuthentication();
app.UseMiddleware<AccesoAplicacionesMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();


// Por defecto se usa el catálogo generado para las aplicaciones web. Los hosts de pruebas de negocio pueden
// proporcionar un catálogo mínimo; las pruebas de archivos WebAssembly verifican el catálogo real.
var manifiestoEstaticos = app.Configuration["StaticAssets:ManifestPath"];
app.MapStaticAssets(manifiestoEstaticos);

app.MapHealthChecks("/health");

app.MapRazorPages()
    .WithStaticAssets(manifiestoEstaticos);

app.MapAdministracionEndpoints()
    .MapEnviosEndpoints()
    .MapPlanificacionEndpoints()
    .MapEjecucionEndpoints()
    .MapSeguimientoEndpoints()
    .MapDepositoEndpoints();

// Aplicaciones Blazor WebAssembly servidas desde el mismo origen que la API (ADR-0001, addendum 5).
// Cada ruta sin extensión de archivo cae en el index.html de su aplicación, que resuelve la navegación.
app.MapFallbackToFile("/portal/{*path:nonfile}", "portal/index.html");
app.MapFallbackToFile("/seguimiento/{*path:nonfile}", "seguimiento/index.html");
app.MapFallbackToFile("/repartidor/{*path:nonfile}", "repartidor/index.html");

app.Run();
