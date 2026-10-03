using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Deposito;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class DepositoModule
{
    public static IServiceCollection AddDepositoModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Registrar acá handlers, DbContext y adaptadores del módulo.
        return services;
    }

    public static IEndpointRouteBuilder MapDepositoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/deposito").WithTags("Deposito");

        // Mapear acá los endpoints de Presentation/Features, por ejemplo: CrearEnvioEndpoint.Map(group);
        return endpoints;
    }
}
