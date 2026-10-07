using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    // Registra el DbContext de un módulo sobre PostgreSQL, con su historial de migraciones en su schema.
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, string? connectionString, string schema)
        where TContext : ModuleDbContext
    {
        // Una sola unidad de trabajo (y una sola conexión) por request, para todos los módulos.
        services.TryAddScoped(_ => new UnidadDeTrabajo(connectionString));
        services.TryAddScoped<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajo>());

        services.AddDbContext<TContext>((sp, options) => options.UseNpgsql(
            sp.GetRequiredService<UnidadDeTrabajo>().Conexion,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema)));

        // Permite que el host migre todos los módulos sin conocer sus DbContext, que son internal.
        services.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<TContext>());

        return services;
    }

    public static async Task MigrateModuleDatabasesAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        foreach (var db in scope.ServiceProvider.GetServices<ModuleDbContext>())
        {
            await db.Database.MigrateAsync();
        }
    }
}
