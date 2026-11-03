using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Logistica.BuildingBlocks.Infrastructure.Configuracion;

// En producción, una cadena de conexión que falta no puede degradar el sistema en silencio: sin Valkey la caché caería a
// la memoria del proceso (CachingExtensions) y sin RabbitMQ la API dejaría de publicar el outbox. Es preferible que el
// proceso no arranque y App Platform muestre el error en el despliegue. En desarrollo y en las pruebas siguen siendo
// opcionales, para depurar sin levantar todos los servicios.
public static class ConexionesObligatorias
{
    public static void Exigir(IConfiguration configuracion, IHostEnvironment entorno, params string[] nombres)
    {
        if (!entorno.IsProduction())
        {
            return;
        }

        var faltan = nombres.Where(nombre => string.IsNullOrWhiteSpace(configuracion.GetConnectionString(nombre))).ToArray();
        if (faltan.Length > 0)
        {
            throw new InvalidOperationException(
                $"En producción son obligatorias las cadenas de conexión {string.Join(", ", faltan)} (ConnectionStrings__<nombre>).");
        }
    }
}
