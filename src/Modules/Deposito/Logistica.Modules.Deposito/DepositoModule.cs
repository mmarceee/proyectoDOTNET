using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Deposito.Contracts.Results;
using Logistica.Modules.Deposito.Application;
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
        services.AddScoped<IDepositoModuleApi, DepositoModuleApi>();

        services.AddScoped<RecibirBultoHandler>();
        services.AddScoped<ConsultarBultoParaRecepcionHandler>();
        services.AddScoped<Application.IResponsableDeposito, Presentation.ResponsableDeposito>();
        services.AddScoped<Application.IOperacionesDeposito, OperacionesDeposito>();
        services.AddScoped<Application.Features.ConciliarBultos.ConciliarBultosHandler>();

        // CU-55: confirmar la rendición.
        services.AddScoped<Application.Features.ConfirmarRendicion.ConfirmarRendicionHandler>();

        // CU-31: confirmar la devolución al comercio.
        services.AddScoped<Application.Features.ConfirmarDevolucion.ConfirmarDevolucionHandler>();

        // CU-32: generar la liquidación de un comercio.
        services.AddScoped<Domain.Liquidaciones.ILiquidacionRepository, LiquidacionRepository>();
        services.AddScoped<Application.Features.GenerarLiquidacion.GenerarLiquidacionHandler>();

        // CU-33: la cuenta corriente del comercio.
        services.AddScoped<Application.Features.ConsultarCuentaCorriente.ConsultarCuentaCorrienteHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapDepositoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/deposito").WithTags("Deposito");

        RecibirBultoEndpoint.Map(group);
        Presentation.Features.ConciliarBultos.ConciliarBultosEndpoint.Map(group);
        Presentation.Features.ConsultarCuentaCorriente.ConsultarCuentaCorrienteEndpoint.Map(endpoints);

        return endpoints;
    }
}
