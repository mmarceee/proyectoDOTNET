using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.ConsultarMisRutas;
using Logistica.Modules.Ejecucion.Application.Features.DescargarHojaDeRuta;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Ejecucion.Presentation.Features.ConsultarMisRutas;

internal static class ConsultarMisRutasEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/api/repartidor/rutas",
        async (int? pagina, HttpContext http, ConsultarMisRutasHandler handler, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (pagina is < 1 or > 10000) return Results.BadRequest();
            if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario)
                || !Guid.TryParse(http.User.FindFirstValue(ClaimsDeSesion.RepartidorId), out var repartidor)) return Results.Forbid();
            try { return Results.Ok(await handler.HandleAsync(usuario, repartidor, pagina ?? 1, ct)); }
            catch (VinculoRepartidorInvalidoException) { return Results.Forbid(); }
        }).RequireAuthorization(Politicas.Repartidor).WithTags("Ejecucion");
}
