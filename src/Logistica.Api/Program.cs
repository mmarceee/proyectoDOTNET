using Logistica.Api.Errores;
using Logistica.Api.Tenancy;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion;
using Logistica.Modules.Deposito;
using Logistica.Modules.Ejecucion;
using Logistica.Modules.Envios;
using Logistica.Modules.Planificacion;
using Logistica.Modules.Seguimiento;
using Logistica.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// Errores como ProblemDetails; las reglas de negocio violadas responden 400.
builder.Services.AddProblemDetails();
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

// Inquilino de cada request. Provisorio hasta Identity (15/10).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, TenantProvisorio>();

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
    .AddDepositoModule(builder.Configuration);

var app = builder.Build();

// En Docker Compose la API aplica las migraciones de todos los módulos al iniciar (docker-compose.yml),
// y después carga los datos iniciales, que necesitan las tablas ya creadas.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateModuleDatabasesAsync();
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
}

app.MapStaticAssets();

app.MapHealthChecks("/health");

app.MapRazorPages()
    .WithStaticAssets();

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
