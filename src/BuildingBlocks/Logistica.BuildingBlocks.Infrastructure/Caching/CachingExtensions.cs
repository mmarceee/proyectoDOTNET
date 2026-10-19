using Logistica.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Logistica.BuildingBlocks.Infrastructure.Caching;

public static class CachingExtensions
{
    // Valkey si hay cadena de conexión (Docker Compose, user-secrets y producción); si no, la memoria del proceso,
    // para depurar sin Valkey y para las pruebas. Valkey usa el protocolo de Redis, por eso el proveedor es el de Redis.
    public static IServiceCollection AddCacheDistribuida(this IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                var configuracion = ConfigurationOptions.Parse(connectionString);

                // La API arranca aunque Valkey no esté, y se reconecta sola cuando vuelve.
                configuracion.AbortOnConnectFail = false;

                // Sin conexión, cada operación falla enseguida en lugar de esperar a que vuelva: la lectura va a la
                // base sin demorar el request. Una caché lenta tampoco lo frena más de un segundo.
                configuracion.BacklogPolicy = BacklogPolicy.FailFast;
                configuracion.ConnectTimeout = 2000;
                configuracion.AsyncTimeout = 1000;

                options.ConfigurationOptions = configuracion;
                options.InstanceName = "logistica:";
            });
        }

        services.AddScoped<ICacheDistribuida, CacheDistribuida>();
        return services;
    }
}
