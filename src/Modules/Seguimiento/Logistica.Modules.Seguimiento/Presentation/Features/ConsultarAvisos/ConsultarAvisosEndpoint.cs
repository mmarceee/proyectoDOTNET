using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarAvisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Seguimiento.Presentation.Features.ConsultarAvisos;

internal static class ConsultarAvisosEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/repartidor/avisos", async (ClaimsPrincipal usuario, int? pagina,
            ConsultarAvisosHandler handler, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!Guid.TryParse(usuario.FindFirstValue(ClaimsDeSesion.RepartidorId), out var repartidor) || repartidor == Guid.Empty)
                return Results.Forbid();
            if (pagina is < 1 or > 10000) return Results.BadRequest("La página debe estar entre 1 y 10000.");
            return Results.Ok(await handler.HandleAsync(repartidor, pagina ?? 1, ct));
        }).WithTags("Seguimiento").RequireAuthorization(Politicas.Repartidor)
            .RequireAuthorization(p => p.RequireAuthenticatedUser());
    }
}
