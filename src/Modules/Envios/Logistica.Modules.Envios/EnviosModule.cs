using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Envios.Application.Features.CrearEnvio;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Envios.Presentation.Features.CrearEnvio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Envios;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class EnviosModule
{
    public static IServiceCollection AddEnviosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EnviosDbContext>(configuration.GetConnectionString("Postgres"), EnviosDbContext.Schema);
        services.AddScoped<IEnvioRepository, EnvioRepository>();

        services.AddScoped<CrearEnvioHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapEnviosEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/envios").WithTags("Envios");

        CrearEnvioEndpoint.Map(group);

        return endpoints;
    }
}
