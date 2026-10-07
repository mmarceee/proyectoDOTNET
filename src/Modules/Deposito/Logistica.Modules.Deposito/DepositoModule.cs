using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Deposito.Application.Features.RecibirBulto;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.Modules.Deposito.Infrastructure.Persistence;
using Logistica.Modules.Deposito.Presentation.Features.RecibirBulto;
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
        services.AddModuleDbContext<DepositoDbContext>(configuration.GetConnectionString("Postgres"), DepositoDbContext.Schema);
        services.AddScoped<IRecepcionRepository, RecepcionRepository>();

        services.AddScoped<RecibirBultoHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapDepositoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/deposito").WithTags("Deposito");

        RecibirBultoEndpoint.Map(group);

        return endpoints;
    }
}
