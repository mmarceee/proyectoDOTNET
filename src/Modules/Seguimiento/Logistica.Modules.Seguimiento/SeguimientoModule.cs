using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarAvisos;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;

namespace Logistica.Modules.Seguimiento;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class SeguimientoModule
{
    public static IServiceCollection AddSeguimientoModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SeguimientoDbContext>(configuration.GetConnectionString("Postgres"), SeguimientoDbContext.Schema);
        services.AddScoped<IAvisosRepository, AvisosRepository>();
        services.AddScoped<ConsultarAvisosHandler>();
        services.AddKeyedScoped<IConsumidorIntegracion, NotificarRutaDespachada>(NotificarRutaDespachada.Cola);
        services.AddSingleton(new SuscripcionIntegracion(NotificarRutaDespachada.Cola, RutaDespachada.Tipo));
        return services;
    }

    public static IEndpointRouteBuilder MapSeguimientoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Presentation.Features.ConsultarAvisos.ConsultarAvisosEndpoint.Map(endpoints);
        return endpoints;
    }
}
