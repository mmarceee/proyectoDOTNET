using System.Security.Claims;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.IniciarDevolucion;
using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.IniciarDevolucion;

// POST /api/envios/{id}/devolucion (CU-20): el comercio pide devolver un envío que está en el depósito (T12).
// El Portal todavía no tiene el detalle del envío, así que por ahora se usa desde la API. Las reglas rotas (sin motivo,
// A1, A2) las traduce DomainExceptionHandler a 400.
internal static class IniciarDevolucionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/devolucion", IniciarAsync).RequireAuthorization(Politicas.Comercio);
    }

    private static async Task<Results<NoContent, NotFound>> IniciarAsync(
        Guid id, IniciarDevolucionRequest request, ClaimsPrincipal usuario, IniciarDevolucionHandler handler,
        CancellationToken ct)
    {
        var responsable = Guid.TryParse(usuario.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : (Guid?)null;
        var iniciada = await handler.HandleAsync(
            new IniciarDevolucionCommand(id, request.Motivo, OrigenEvento.PortalComercio, responsable), ct);

        // 404 genérico: no distingue un envío inexistente de uno de otro inquilino o comercio (ADR-0002, sección 2.9).
        return iniciada ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
