using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;

public static class FallidosExtensions
{
    // CU-09: la tabla de fallidos y su consulta. El registrador (RegistradorFallidos) se agrega aparte, sólo con RabbitMQ.
    public static IServiceCollection AddMensajesFallidos(this IServiceCollection services, string? connectionString)
    {
        services.AddModuleDbContext<MensajeriaDbContext>(connectionString, MensajeriaDbContext.Schema);
        services.AddScoped<IMensajesFallidos, MensajesFallidos>();
        return services;
    }
}
