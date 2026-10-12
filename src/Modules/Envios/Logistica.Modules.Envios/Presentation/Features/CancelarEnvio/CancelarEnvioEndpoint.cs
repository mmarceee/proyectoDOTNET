using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Envios.Application.Features.CancelarEnvio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.CancelarEnvio;

// POST /api/envios/{id}/cancelacion (CU-16). Endpoint delgado: traduce HTTP al command y llama al handler.
// La transición inválida (A1) la traduce DomainExceptionHandler a 400, y la concurrencia, ConcurrenciaExceptionHandler a 409.
internal static class CancelarEnvioEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/cancelacion", CancelarAsync);
    }

    private static async Task<Results<NoContent, NotFound>> CancelarAsync(
        Guid id, CancelarEnvioRequest? request, CancelarEnvioHandler handler, CancellationToken ct)
    {
        var cancelado = await handler.HandleAsync(new CancelarEnvioCommand(id, request?.Motivo), ct);

        // 404 genérico: no distingue un envío inexistente de uno de otro inquilino (ADR-0002, sección 2.9).
        return cancelado ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
