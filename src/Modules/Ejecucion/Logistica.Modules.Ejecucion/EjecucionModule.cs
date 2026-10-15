using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Ejecucion.Infrastructure;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Presentation.Features.SincronizarOperaciones;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Ejecucion;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class EjecucionModule
{
    public static IServiceCollection AddEjecucionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EjecucionDbContext>(configuration.GetConnectionString("Postgres"), EjecucionDbContext.Schema);
        services.AddScoped<IOperacionRepository, OperacionRepository>();

        // CU-52 (ADR-0004): el lote y cada operación en su propio ámbito.
        services.AddScoped<SincronizarOperacionesHandler>();
        services.AddScoped<ProcesarOperacionHandler>();
        services.AddScoped<IProcesadorDeOperacion, ProcesadorEnAmbitoPropio>();

        return services;
    }

    public static IEndpointRouteBuilder MapEjecucionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // La PWA del repartidor sincroniza en /api/repartidor (ADR-0004, sección 2.9).
        SincronizarOperacionesEndpoint.Map(endpoints);

        return endpoints;
    }
}
