using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Seguimiento.Presentation;

internal static class AccesoAvisos
{
    public static RouteGroupBuilder CrearGrupo(IEndpointRouteBuilder endpoints, string ruta)
    {
        var grupo = endpoints.MapGroup(ruta).WithTags("Avisos").RequireAuthorization(Politicas.Comercio)
            .RequireAuthorization(p => p.RequireAuthenticatedUser().RequireClaim(ClaimsDeSesion.ComercioId));
        grupo.AddEndpointFilter(async (contexto, siguiente) =>
        {
            var http = contexto.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsPost(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException)
                { return Results.Problem(statusCode: 400, title: "La solicitud no tiene un token válido."); }
            }
            return await siguiente(contexto);
        });
        return grupo;
    }
}
