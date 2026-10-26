using Logistica.Modules.Seguimiento.Application.Features.VerTablero;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Seguimiento.Presentation.Features.VerTablero;

// CU-66: GET /api/tablero, la foto completa. Sólo Despachador o Administrador (pre-condición 1); la página la pide al
// abrir y al reconectarse (A1).
internal static class ConsultarTableroEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tablero", async (ConsultarTableroHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(ct)))
            .WithTags("Seguimiento")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Administrador,Despachador" });

        // ADR-0004, sección 2.5: marcar como revisado un conflicto de sincronización. 204, o 404 si no existe o es de
        // otro operador; una operación que no fue rechazada responde 400 (DomainException).
        endpoints.MapPost("/api/tablero/conflictos/{operacionId:guid}/revision",
                async (Guid operacionId, RevisarConflictoHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(operacionId, ct) ? Results.NoContent() : Results.NotFound())
            .WithTags("Seguimiento")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Administrador,Despachador" });
    }
}
