using Logistica.Modules.Administracion;
using Logistica.Modules.Deposito;
using Logistica.Modules.Ejecucion;
using Logistica.Modules.Envios;
using Logistica.Modules.Planificacion;
using Logistica.Modules.Seguimiento;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services
    .AddAdministracionModule(builder.Configuration)
    .AddEnviosModule(builder.Configuration)
    .AddPlanificacionModule(builder.Configuration)
    .AddEjecucionModule(builder.Configuration)
    .AddSeguimientoModule(builder.Configuration)
    .AddDepositoModule(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapAdministracionEndpoints()
    .MapEnviosEndpoints()
    .MapPlanificacionEndpoints()
    .MapEjecucionEndpoints()
    .MapSeguimientoEndpoints()
    .MapDepositoEndpoints();

app.Run();
