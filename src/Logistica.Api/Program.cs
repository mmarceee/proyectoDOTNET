using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion;
using Logistica.Modules.Deposito;
using Logistica.Modules.Ejecucion;
using Logistica.Modules.Envios;
using Logistica.Modules.Planificacion;
using Logistica.Modules.Seguimiento;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

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

// En Docker Compose la API aplica las migraciones de todos los módulos al iniciar (docker-compose.yml).
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateModuleDatabasesAsync();
}

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
