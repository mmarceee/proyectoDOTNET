using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.ReprogramarEnvio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.ReprogramarEnvio;

// POST /api/envios/{id}/reprogramacion (CU-19). Endpoint delgado: traduce HTTP al command y llama al handler.
// A1 (antes del plazo entre intentos) o una franja que no aplica: DomainExceptionHandler responde 400.
internal static class ReprogramarEnvioEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // CU-19: la reprogramación manual la hace el despachador.
        group.MapPost("/{id:guid}/reprogramacion", ReprogramarAsync).RequireAuthorization(Politicas.Despachador);
    }

    private static async Task<Results<NoContent, NotFound>> ReprogramarAsync(
        Guid id, ReprogramarEnvioRequest request, ReprogramarEnvioHandler handler, CancellationToken ct)
    {
        var reprogramado = await handler.HandleAsync(new ReprogramarEnvioCommand(id, request.Fecha, request.FranjaId), ct);

        // 404 genérico: no distingue un envío inexistente de uno de otro operador (ADR-0002, sección 2.9).
        return reprogramado ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
