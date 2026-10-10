using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Application;
using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Administracion;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class AdministracionModule
{
    public static IServiceCollection AddAdministracionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AdministracionDbContext>(
            configuration.GetConnectionString("Postgres"), AdministracionDbContext.Schema);

        services.AddScoped<DatosIniciales>();
        services.AddScoped<IRecursosPlanificacionReader, RecursosPlanificacionReader>();
        services.AddScoped<Contracts.IAdministracionModuleApi, AdministracionModuleApi>();

        return services;
    }

    public static IEndpointRouteBuilder MapAdministracionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/administracion").WithTags("Administracion");

        // Mapear acá los endpoints de Presentation/Features, por ejemplo: CrearEnvioEndpoint.Map(group);
        return endpoints;
    }

    // Carga los datos iniciales. La llama el host al iniciar, después de las migraciones (ADR-0002, sección 2.8).
    public static async Task SembrarDatosInicialesAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DatosIniciales>().SembrarAsync(ct);
    }
}
