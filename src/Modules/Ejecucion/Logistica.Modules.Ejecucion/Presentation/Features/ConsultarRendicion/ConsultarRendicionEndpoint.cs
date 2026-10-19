using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.ConsultarRendicion;
using Logistica.Modules.Ejecucion.Application.Features.DescargarHojaDeRuta;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Ejecucion.Presentation.Features.ConsultarRendicion;

internal static class ConsultarRendicionEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/api/repartidor/rutas/{rutaId:guid}/rendicion",
        async (Guid rutaId, HttpContext http, ConsultarRendicionHandler handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario)
                || !Guid.TryParse(http.User.FindFirstValue(ClaimsDeSesion.RepartidorId), out var repartidor)) return Results.Forbid();
            try
            {
                var r = await handler.HandleAsync(usuario, repartidor, rutaId, ct);
                return r is null ? Results.NotFound() : Results.Ok(r);
            }
            catch (VinculoRepartidorInvalidoException) { return Results.Forbid(); }
        }).RequireAuthorization(Politicas.Repartidor).WithTags("Ejecucion");
}
