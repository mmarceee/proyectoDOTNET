using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Api.Errores;

// Otra operación cambió el mismo registro entre que se leyó y se guardó (concurrencia optimista,
// por ejemplo una recepción y una cancelación del mismo envío). Responde 409: el cliente vuelve a intentar.
internal sealed class ConcurrenciaExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Otra operación modificó los mismos datos.",
                Detail = "Los datos cambiaron mientras se procesaba la solicitud. Volvé a intentarlo.",
            },
        });
    }
}
