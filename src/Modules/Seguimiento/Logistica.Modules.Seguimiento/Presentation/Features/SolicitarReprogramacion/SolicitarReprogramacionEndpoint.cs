using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Seguimiento.Application.Features.SolicitarReprogramacion;
using Logistica.Modules.Seguimiento.Contracts;
using Logistica.Modules.Seguimiento.Presentation.Features.ConsultarSeguimiento;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Seguimiento.Presentation.Features.SolicitarReprogramacion;

// CU-61: GET y POST /api/seguimiento/{token}/reprogramacion, sin usuario: como en CU-60, el token firmado es la única
// credencial y fija el inquilino (InquilinoDelToken), y el límite de tasa también cubre esta operación (requerimiento
// especial 3). Un envío que no existe responde igual que un enlace inválido; una regla rota, 400 con su motivo.
internal static class SolicitarReprogramacionEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/api/seguimiento/{token}/reprogramacion")
            .WithTags("Seguimiento")
            .AllowAnonymous()
            .RequireRateLimiting(LimitesSeguimiento.Politica)
            .AddEndpointFilter<InquilinoDelToken>();

        grupo.MapGet("/", async (string token, HttpContext http, SolicitarReprogramacionHandler handler,
                CancellationToken ct) =>
            await handler.OpcionesAsync(EnvioDe(http), ct) is { } opciones
                ? Results.Ok(opciones)
                : InquilinoDelToken.EnlaceNoValido());

        grupo.MapPost("/", async (string token, SolicitarReprogramacionRequest pedido, HttpContext http,
                SolicitarReprogramacionHandler handler, CancellationToken ct) =>
            await handler.ReprogramarAsync(EnvioDe(http), pedido, ct)
                ? Results.NoContent()
                : InquilinoDelToken.EnlaceNoValido());
    }

    private static Guid EnvioDe(HttpContext http) => (Guid)http.Items[InquilinoDelToken.ClaveEnvio]!;
}
