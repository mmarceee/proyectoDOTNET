using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Seguimiento.Application;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
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

        // CU-60: la clave se lee recién cuando se usa, así la API arranca aunque falte y el error dice qué configurar.
        services.AddSingleton(_ => new ClaveSeguimiento(configuration["Seguimiento:ClaveToken"]));
        services.AddSingleton<TokenSeguimiento>();
        services.AddScoped<Contracts.ISeguimientoModuleApi, SeguimientoModuleApi>();

        // El handler necesita los contratos de Envíos y Administración, que sólo están en la API. Con una fábrica, el
        // Worker (que también registra este módulo) no intenta validarlo al arrancar.
        services.AddScoped(sp => new ConsultarSeguimientoHandler(
            sp.GetRequiredService<Envios.Contracts.IEnviosModuleApi>(),
            sp.GetRequiredService<Administracion.Contracts.IAdministracionModuleApi>(),
            sp.GetRequiredService<SharedKernel.ICacheDistribuida>()));

        // CU-60: cada cambio de estado de un envío borra su seguimiento de la caché; una cola por tipo de evento.
        foreach (var tipo in InvalidarSeguimiento.Tipos)
        {
            services.AddKeyedScoped<IConsumidorIntegracion, InvalidarSeguimiento>(InvalidarSeguimiento.Cola(tipo));
            services.AddSingleton(new SuscripcionIntegracion(InvalidarSeguimiento.Cola(tipo), tipo));
        }

        return services;
    }

    public static IEndpointRouteBuilder MapSeguimientoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Presentation.Features.ConsultarAvisos.ConsultarAvisosEndpoint.Map(endpoints);
        Presentation.Features.ConsultarSeguimiento.ConsultarSeguimientoEndpoint.Map(endpoints);
        return endpoints;
    }
}
