using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Logistica.Modules.Administracion.Presentation.Features.IniciarSesion;

// Sólo para las páginas de ingreso. Nunca acepta el POST rechazado ni reutiliza sus credenciales:
// vuelve al GET para emitir un formulario válido para la sesión actual.
internal sealed class FormularioIngresoVencidoFilter : IAsyncAlwaysRunResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            var request = context.HttpContext.Request;
            context.Result = new LocalRedirectResult($"{request.PathBase}{request.Path}?formularioVencido=true");
        }
        await next();
    }
}
