using System.Globalization;
using System.Threading.RateLimiting;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Logistica.Api.ApiPublica;

internal static class LimitesApiPublica
{
    public const string Politica = "ApiPublica";

    public static void Configurar(RateLimiterOptions opciones, IConfiguration configuracion)
    {
        var produccion = Produccion(configuracion);
        var prueba = Prueba(configuracion);
        opciones.AddPolicy(Politica, http =>
        {
            var esPrueba = http.User.FindFirst(AccesoApiPublica.AmbienteClaim)?.Value == "true";
            var particion = (http.User.FindFirst(ClaimsDeSesion.OperadorId)?.Value,
                http.User.FindFirst(ClaimsDeSesion.ComercioId)?.Value, esPrueba);
            return RateLimitPartition.GetFixedWindowLimiter(particion, _ => Ventana(esPrueba ? prueba : produccion));
        });
        opciones.OnRejected = (rechazo, _) =>
        {
            if (rechazo.HttpContext.Request.Path.StartsWithSegments("/api/v1"))
                EscribirEspera(rechazo.HttpContext, rechazo.Lease);
            return ValueTask.CompletedTask;
        };
    }

    public static int Produccion(IConfiguration configuracion) => Leer(configuracion, "ProduccionPorMinuto", 120);
    public static int Prueba(IConfiguration configuracion) => Leer(configuracion, "PruebaPorMinuto", 30);
    public static int PorIp(IConfiguration configuracion) => Leer(configuracion, "SolicitudesPorIpPorMinuto", 600);

    private static int Leer(IConfiguration configuracion, string nombre, int valorInicial)
    {
        var valor = configuracion.GetValue($"ApiPublica:{nombre}", valorInicial);
        return valor > 0 ? valor : throw new InvalidOperationException($"ApiPublica:{nombre} debe ser mayor que cero.");
    }

    public static FixedWindowRateLimiterOptions Ventana(int cuota) => new()
    {
        PermitLimit = cuota,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    };

    public static void EscribirEspera(HttpContext http, RateLimitLease lease)
    {
        var segundos = lease.TryGetMetadata(MetadataName.RetryAfter, out var espera)
            ? Math.Max(1, Math.Ceiling(espera.TotalSeconds)) : 60;
        http.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
    }
}

internal sealed class LimitarSolicitudesApiPublica
{
    private readonly RequestDelegate _siguiente;
    private readonly PartitionedRateLimiter<HttpContext> _porIp;

    public LimitarSolicitudesApiPublica(RequestDelegate siguiente, IConfiguration configuracion, IHostApplicationLifetime vida)
    {
        _siguiente = siguiente;
        var cuota = LimitesApiPublica.PorIp(configuracion);
        // Cubre también claves ausentes o inválidas, antes de la consulta por hash.
        _porIp = PartitionedRateLimiter.Create<HttpContext, string>(
            http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
                _ => LimitesApiPublica.Ventana(cuota)));
        vida.ApplicationStopped.Register(_porIp.Dispose);
    }

    public async Task InvokeAsync(HttpContext http)
    {
        if (http.Request.Path.StartsWithSegments("/api/v1"))
        {
            http.Response.OnStarting(() =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
            using var lease = await _porIp.AcquireAsync(http, cancellationToken: http.RequestAborted);
            if (!lease.IsAcquired)
            {
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                LimitesApiPublica.EscribirEspera(http, lease);
                return;
            }
        }
        await _siguiente(http);
    }
}
