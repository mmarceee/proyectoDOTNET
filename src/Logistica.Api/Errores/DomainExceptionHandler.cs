using Logistica.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;

namespace Logistica.Api.Errores;

// Convierte una regla de negocio violada en un 400 con ProblemDetails, para todos los módulos.
// Las demás excepciones siguen de largo y terminan en un 500 sin detalles internos.
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "La solicitud no cumple una regla de negocio.",
                Detail = exception.Message,
            },
        });
    }
}
