using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace Logistica.Api.Errores;

// Pruebas de resiliencia (opcional 7.4): con PostgreSQL caído no hay nada que hacer sin la base, pero el cliente recibe
// un 503 con Retry-After en lugar de un 500, y sabe que puede volver a intentar. La API sigue viva y se recupera sola
// cuando la base vuelve, sin reiniciarse. EF Core puede envolver el error (DbUpdateException), por eso se recorren las
// excepciones internas. Un error de SQL (PostgresException no transitoria) sigue siendo un 500: es un defecto, no una caída.
internal sealed class BaseDeDatosNoDisponibleExceptionHandler(IProblemDetailsService problemDetails,
    ILogger<BaseDeDatosNoDisponibleExceptionHandler> log) : IExceptionHandler
{
    public const int SegundosParaReintentar = 5;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (!EsCaidaDeLaBase(exception))
        {
            return false;
        }

        log.LogWarning(exception, "PostgreSQL no está disponible; se responde 503.");
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = SegundosParaReintentar.ToString();

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "El servicio no está disponible en este momento.",
                Detail = "No se pudo acceder a los datos. Volvé a intentarlo en unos segundos.",
            },
        });
    }

    // Una falla de conexión (NpgsqlException que no vino del servidor) o un error del servidor marcado como
    // transitorio, por ejemplo el que manda PostgreSQL al apagarse con conexiones abiertas.
    internal static bool EsCaidaDeLaBase(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is PostgresException servidor)
            {
                return servidor.IsTransient || servidor.SqlState.StartsWith("57P", StringComparison.Ordinal);
            }

            if (e is NpgsqlException)
            {
                return true;
            }
        }

        return false;
    }
}
