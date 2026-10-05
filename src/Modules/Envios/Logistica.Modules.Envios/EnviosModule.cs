using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;

namespace Logistica.Modules.Envios;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class EnviosModule
{
    public static IServiceCollection AddEnviosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EnviosDbContext>(configuration.GetConnectionString("Postgres"), EnviosDbContext.Schema);
        services.AddScoped<IEnvioRepository, EnvioRepository>();

        services.AddScoped<IEnviosListadoReader, EnviosListadoReader>();
        services.AddScoped<ConsultarEnviosHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapEnviosEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/envios").WithTags("Envios");

        // Mapear acá los endpoints de Presentation/Features, por ejemplo: CrearEnvioEndpoint.Map(group);
        return endpoints;
    }
}
