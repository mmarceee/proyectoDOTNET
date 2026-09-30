using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Seguimiento;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class SeguimientoModule
{
    public static IServiceCollection AddSeguimientoModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Registrar acá handlers, DbContext y adaptadores del módulo.
        return services;
    }

    public static IEndpointRouteBuilder MapSeguimientoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/seguimiento").WithTags("Seguimiento");

        // Mapear acá los endpoints de Presentation/Features, por ejemplo: CrearEnvioEndpoint.Map(group);
        return endpoints;
    }
}
