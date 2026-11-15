using Logistica.Api.Errores;
using Logistica.Api.Tenancy;
using Logistica.Api.Seguridad;
using Logistica.Api.ApiPublica;
using Logistica.Api.Salud;
using Logistica.BuildingBlocks.Infrastructure.Caching;
using Logistica.BuildingBlocks.Infrastructure.Configuracion;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;
using Logistica.BuildingBlocks.Infrastructure.Observabilidad;
using Logistica.Modules.Administracion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito;
using Logistica.Modules.Ejecucion;
using Logistica.Modules.Envios;
using Logistica.Modules.Planificacion;
using System.Threading.RateLimiting;
using Logistica.Modules.Seguimiento;
using Logistica.Modules.Seguimiento.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Registro estructurado con Serilog, con la traza de cada evento y, si hay OTLP, enviado a Aspire Dashboard (letra 6.12).
builder.Services.AddRegistroEstructurado(builder.Configuration, "logistica-api");

// /health chequea sólo PostgreSQL, con un límite de 3 s para que una base colgada no lo deje esperando (PostgresHealthCheck).
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>(PostgresHealthCheck.Nombre, timeout: TimeSpan.FromSeconds(3));

// Trazas distribuidas (letra, sección 6.12): cada request, sus consultas a PostgreSQL y las fuentes propias de los
// módulos, como la sincronización de la PWA (ADR-0004, sección 2.9), y la publicación del outbox, que el Worker
// continúa (ADR-0003, sección 2.9). Métricas del tablero técnico: latencia media, p95 y p99 y tasa de errores
// (MetricasHttp, más las de ASP.NET Core), sincronizaciones pendientes (Ejecución), relación de aciertos de la caché y
// profundidad de la cola de fallidos (MetricasMensajeria). Se exportan por OTLP a Aspire Dashboard cuando
// OTEL_EXPORTER_OTLP_ENDPOINT está configurado (docker-compose.yml y Terraform).
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
        metricas.AddAspNetCoreInstrumentation()
            .AddMeter(MetricasHttp.Medidor, MetricasCache.Medidor, MetricasMensajeria.Medidor, EjecucionModule.FuenteDeTrazas);

        if (exportarOtlp)
        {
            metricas.AddOtlpExporter();
        }
    });

// En producción no se arranca sin la base, Valkey ni RabbitMQ (ConexionesObligatorias).
ConexionesObligatorias.Exigir(builder.Configuration, builder.Environment, "Postgres", "Valkey", "RabbitMQ");

// Caché distribuida: Valkey si hay cadena de conexión; si no, la memoria del proceso (CachingExtensions).
builder.Services.AddCacheDistribuida(builder.Configuration.GetConnectionString("Valkey"));

// Claves de Data Protection en PostgreSQL, para que la sesión sobreviva a un nuevo despliegue (ClavesProteccionDbContext).
builder.Services.AddDbContext<ClavesProteccionDbContext>(opciones => opciones.UseNpgsql(
    builder.Configuration.GetConnectionString("Postgres"),
    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ClavesProteccionDbContext.Schema)));
builder.Services.AddDataProtection()
    .SetApplicationName("logistica")
    .PersistKeysToDbContext<ClavesProteccionDbContext>();

// Errores como ProblemDetails; las reglas de negocio violadas responden 400. Una escritura sobre datos
// de otro inquilino responde 404, igual que un recurso inexistente (ADR-0002, sección 2.9).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TenantMismatchExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrenciaExceptionHandler>();
// PostgreSQL caído: 503 con Retry-After en lugar de 500 (pruebas de resiliencia, opcional 7.4).
builder.Services.AddExceptionHandler<BaseDeDatosNoDisponibleExceptionHandler>();

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
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AutenticarClaveApi>(
        AutenticarClaveApi.Esquema, _ => { });
builder.Services.AddAuthorization(opciones => opciones.AddPolicy(AutenticarClaveApi.Esquema,
    politica => politica.AddAuthenticationSchemes(AutenticarClaveApi.Esquema).RequireAuthenticatedUser()));
builder.Services.AddAuthorization(opciones => opciones.AddPolicy(DocumentacionApi.SoloPruebas,
    politica => politica.RequireClaim(AccesoApiPublica.AmbienteClaim, "true")));
DocumentacionApi.Configurar(builder.Services, builder.Configuration);

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
    LimitesApiPublica.Configurar(opciones, builder.Configuration);
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
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider.GetRequiredService<ClavesProteccionDbContext>().Database.MigrateAsync();
    }
    await app.Services.SembrarDatosInicialesAsync();
}

// Primero de la cadena, para medir la solicitud completa con su código de respuesta final (MetricasHttp).
app.UseMetricasHttp();

// Cabeceras seguras en todas las respuestas, también en los errores y los archivos estáticos (letra 6.3).
app.UseMiddleware<CabecerasSeguridadMiddleware>();

// Una línea estructurada por request (método, ruta, código y duración), con su TraceId (Serilog). Usa el logger de la
// aplicación y no el estático, que queda sin configurar (RegistroEstructurado preserva el estático).
app.UseSerilogRequestLogging(opciones => opciones.Logger = app.Services.GetRequiredService<Serilog.ILogger>());

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

app.UseWhen(http => http.Request.Path.StartsWithSegments("/api/v1"), publica => publica.UseStatusCodePages());
// Lee la cookie de sesión en cada request y arma el usuario, antes de que llegue a cualquier endpoint o página.
app.UseAuthentication();
app.UseMiddleware<AccesoAplicacionesMiddleware>();
app.UseMiddleware<LimitarSolicitudesApiPublica>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<RegistrarUsoClaveApi>();


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

var apiPublica = app.MapGroup("/api/v1")
    .WithGroupName(DocumentacionApi.Version)
    .RequireAuthorization(AutenticarClaveApi.Esquema)
    .RequireRateLimiting(LimitesApiPublica.Politica);
apiPublica.MapEnviosApiPublica();
apiPublica.MapGroup("/pruebas").RequireAuthorization(DocumentacionApi.SoloPruebas)
    .ExcludeFromDescription().MapEnviosApiPublica(esConsola: true);
DocumentacionApi.Map(app);

// Aplicaciones Blazor WebAssembly servidas desde el mismo origen que la API (ADR-0001, addendum 5).
// Cada ruta sin extensión de archivo cae en el index.html de su aplicación, que resuelve la navegación.
app.MapFallbackToFile("/portal/{*path:nonfile}", "portal/index.html");
app.MapFallbackToFile("/seguimiento/{*path:nonfile}", "seguimiento/index.html");
app.MapFallbackToFile("/repartidor/{*path:nonfile}", "repartidor/index.html");

app.Run();
