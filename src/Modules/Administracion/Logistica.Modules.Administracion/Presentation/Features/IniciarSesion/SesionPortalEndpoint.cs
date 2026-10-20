using Logistica.Http.Contracts.Cuenta;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Administracion.Presentation.Features.IniciarSesion;

internal static class SesionPortalEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/portal/sesion", (HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new SesionPortalResponse(http.User.Identity?.Name));
        }).RequireAuthorization(Politicas.Comercio).WithTags("Administracion");
    }
}
