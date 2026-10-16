using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento;
using Logistica.SharedKernel;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddScoped<InquilinoMensaje>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<InquilinoMensaje>());
builder.Services.AddSeguimientoModule(builder.Configuration);
builder.Services.AddHostedService<ConsumidorRabbitMq>();

var host = builder.Build();
host.Run();
