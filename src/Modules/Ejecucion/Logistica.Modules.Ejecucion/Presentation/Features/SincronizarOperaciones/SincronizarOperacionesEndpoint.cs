using System.Security.Claims;
using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Ejecucion.Presentation.Features.SincronizarOperaciones;

// POST /api/repartidor/sincronizacion (CU-52; ADR-0004, sección 2.9). Endpoint delgado: toma el repartidor de la
// sesión, nunca del cuerpo, y llama al handler. Responde 200 con el resultado de cada operación, aunque alguna se
// rechace: el rechazo es parte de la política, no un error del pedido.
internal static class SincronizarOperacionesEndpoint
{
    // ADR-0004, sección 2.8: hasta 20 operaciones por lote.
    public const int MaximoOperaciones = 20;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/repartidor/sincronizacion", SincronizarAsync)
            .WithTags("Ejecucion")
            .RequireAuthorization(Politicas.Repartidor);
    }

    private static async Task<Results<Ok<SincronizacionResponse>, ValidationProblem, ForbidHttpResult>> SincronizarAsync(
        SincronizacionRequest request, ClaimsPrincipal usuario, SincronizarOperacionesHandler handler, CancellationToken ct)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(ClaimsDeSesion.RepartidorId), out var repartidorId))
        {
            return TypedResults.Forbid();
        }

        if (request.Operaciones.Count is 0 or > MaximoOperaciones)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Operaciones)] = [$"El lote debe tener entre 1 y {MaximoOperaciones} operaciones."],
            });
        }

        var usuarioId = Guid.TryParse(usuario.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;

        return TypedResults.Ok(await handler.HandleAsync(
            new SincronizarOperacionesCommand(repartidorId, usuarioId, request.Operaciones), ct));
    }
}
