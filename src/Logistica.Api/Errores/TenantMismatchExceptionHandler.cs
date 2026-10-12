using Logistica.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;

namespace Logistica.Api.Errores;

// Una escritura sobre datos de otro inquilino (la rechaza TenantSaveChangesInterceptor, o AgregarAlOutbox sin
// operador) responde igual que un recurso inexistente: 404 sin cuerpo, como los NotFound() de los endpoints.
// Nunca 403 ni un mensaje que confirme que el recurso existe (ADR-0002, sección 2.9). El detalle queda en el log,
// para auditoría.
internal sealed class TenantMismatchExceptionHandler(ILogger<TenantMismatchExceptionHandler> logger) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not TenantMismatchException)
        {
            return ValueTask.FromResult(false);
        }

        // Los manejadores de excepciones son singleton: el inquilino, que es scoped, se pide al request.
        var tenant = httpContext.RequestServices.GetRequiredService<ICurrentTenant>();

        // El mensaje de la excepción dice qué entidad y qué inquilino se intentó escribir.
        logger.LogWarning(exception,
            "Escritura rechazada por aislamiento de inquilino: {Metodo} {Ruta}, sesión del operador {OperadorId} y del comercio {ComercioId}.",
            httpContext.Request.Method, httpContext.Request.Path, tenant.OperadorId, tenant.ComercioId);

        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        return ValueTask.FromResult(true);
    }
}
