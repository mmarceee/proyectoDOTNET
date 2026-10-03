using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    // Registra el DbContext de un módulo sobre PostgreSQL, con su historial de migraciones en su schema.
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, string? connectionString, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema)));

        // Permite que el host migre todos los módulos sin conocer sus DbContext, que son internal.
        services.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<TContext>());

        return services;
    }

    public static async Task MigrateModuleDatabasesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();

        foreach (var db in scope.ServiceProvider.GetServices<ModuleDbContext>())
        {
            await db.Database.MigrateAsync();
        }
    }
}
