using Logistica.SharedKernel;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Logistica.Modules.Planificacion.Presentation;

internal sealed class AccesoPlanificacion(IHostEnvironment environment, IConfiguration configuration, ICurrentTenant tenant,
    IAntiforgery antiforgery) : IEndpointFilter, IAsyncPageFilter
{
    public bool Desarrollo => environment.IsDevelopment() && configuration.GetValue<bool>("Planificacion:HabilitarDesarrolloSinIdentity");
    private int? Error(HttpContext http)
    {
        if (!Desarrollo && http.User.Identity?.IsAuthenticated != true) return 401;
        if (!Desarrollo && !http.User.IsInRole("Despachador")) return 403;
        if (tenant.OperadorId is null || tenant.ComercioId is not null) return 403;
        return null;
    }
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (Error(context.HttpContext) is int status) return Results.Problem(statusCode: status, title: status == 401 ? "Iniciá sesión para planificar." : "Acceso no permitido.");
        if (context.HttpContext.Request.Method is "POST" or "PUT" && context.HttpContext.Request.Headers.ContainsKey("Cookie"))
        {
            try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "La solicitud no tiene un token válido."); }
        }
        return await next(context);
    }
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (Error(context.HttpContext) is int status) { context.Result = new StatusCodeResult(status); return; }
        await next();
    }
}
