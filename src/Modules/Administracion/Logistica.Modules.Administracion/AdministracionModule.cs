using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Application;
using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

        // Identity (CU-02): usuarios y perfiles en su propio DbContext. No usa la unidad de trabajo de los módulos.
        services.AddDbContext<IdentidadDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("Postgres"),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentidadDbContext.Schema)));

        services.AddIdentityCore<Usuario>(opciones =>
        {
            // Un correo, un usuario en todo el sistema (ADR-0002, sección 5).
            opciones.User.RequireUniqueEmail = true;
            opciones.Lockout.MaxFailedAccessAttempts = 5;
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentidadDbContext>();


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


    // Crea o actualiza las tablas de Identity. La llama el host junto con las migraciones de los módulos,
    // porque IdentidadDbContext no es un ModuleDbContext y MigrateModuleDatabasesAsync no lo ve.
    public static async Task MigrarIdentidadAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().Database.MigrateAsync(ct);
    }
}
