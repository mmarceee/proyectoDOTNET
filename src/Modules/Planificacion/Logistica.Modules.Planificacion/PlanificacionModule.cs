using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Application.Features.ArmarRuta;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;
using Logistica.Modules.Planificacion.Application.Features.ConsultarEnviosDisponibles;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Presentation;
using Logistica.Modules.Planificacion.Presentation.Features.ArmarRuta;
using Logistica.Modules.Planificacion.Presentation.Features.ConsultarRutas;
using Logistica.Modules.Planificacion.Presentation.Features.ConsultarEnviosDisponibles;
using Logistica.Modules.Planificacion.Presentation.Features.ConsultarRecursosPlanificacion;

namespace Logistica.Modules.Planificacion;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class PlanificacionModule
{
    public static IServiceCollection AddPlanificacionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PlanificacionDbContext>(configuration.GetConnectionString("Postgres"), PlanificacionDbContext.Schema);
        services.AddScoped<IRutaRepository, RutaRepository>();
        services.AddScoped<IConfirmacionPlanificacion, ConfirmacionPlanificacion>();
        services.AddScoped<IResponsablePlanificacion, ResponsablePlanificacion>();
        services.AddScoped<ArmarRutaHandler>();
        services.AddScoped<ConsultarRutasHandler>();
        services.AddScoped<ConsultarEnviosDisponiblesHandler>();
        services.AddScoped<ConsultarRecursosPlanificacionHandler>();
        services.AddScoped<Application.Services.PreparadorEnviosRuta>();
        services.AddScoped<Application.Services.CalendarioPlanificacion>();
        services.AddScoped<AccesoPlanificacion>();
        services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        return services;
    }

    public static IEndpointRouteBuilder MapPlanificacionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/planificacion").WithTags("Planificacion");

        PlanificacionEndpointFilters.Add(group);
        ArmarRutaEndpoint.Map(group);
        ConsultarRutasEndpoint.Map(group);
        ConsultarEnviosDisponiblesEndpoint.Map(group);
        ConsultarRecursosPlanificacionEndpoint.Map(group);
        return endpoints;
    }
}
