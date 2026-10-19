using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.DescargarHojaDeRuta;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Logistica.Modules.Ejecucion.Presentation.Features.DescargarHojaDeRuta;

internal static class DescargarHojaDeRutaEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/repartidor/sesion", (HttpContext http, DescargarHojaDeRutaHandler handler, CancellationToken ct)
            => ConsultarAsync(http, handler, true, ct)).RequireAuthorization(Politicas.Repartidor).WithTags("Ejecucion");
        endpoints.MapGet("/api/repartidor/hoja-de-ruta", (HttpContext http, DescargarHojaDeRutaHandler handler, CancellationToken ct)
            => ConsultarAsync(http, handler, false, ct)).RequireAuthorization(Politicas.Repartidor).WithTags("Ejecucion");
        endpoints.MapGet("/api/repartidor/rutas/{rutaId:guid}/hoja-de-ruta", (Guid rutaId, HttpContext http,
            DescargarHojaDeRutaHandler handler, CancellationToken ct) => ConsultarAsync(http, handler, false, ct, rutaId))
            .RequireAuthorization(Politicas.Repartidor).WithTags("Ejecucion");
    }

    private static async Task<IResult> ConsultarAsync(HttpContext http, DescargarHojaDeRutaHandler handler, bool sesion, CancellationToken ct, Guid? rutaId = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario)
            || !Guid.TryParse(http.User.FindFirstValue(ClaimsDeSesion.RepartidorId), out var repartidor))
            return Results.Forbid();
        try
        {
            if (sesion)
            {
                var datos = await handler.SesionAsync(usuario, repartidor, ct);
                return Results.Ok(datos with { NombreUsuario = http.User.Identity?.Name });
            }
            var hoja = await handler.HandleAsync(usuario, repartidor, ct, rutaId);
            return hoja is null ? Results.NoContent() : Results.Ok(hoja);
        }
        catch (VinculoRepartidorInvalidoException) { return Results.Forbid(); }
        catch (HojaIncompletaException)
        {
            return Results.Problem(statusCode: 503, title: "La hoja de ruta está incompleta.",
                detail: "No se pudo descargar la hoja completa. La copia anterior puede seguir utilizándose sin conexión.");
        }
    }
}
