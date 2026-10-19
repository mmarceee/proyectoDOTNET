using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.Modules.Seguimiento.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Seguimiento.Presentation.Features.ConsultarSeguimiento;

// CU-60: GET /api/seguimiento/{token}, sin autenticación (el token firmado es la única credencial, requerimiento 1).
// InquilinoDelToken valida el token y fija el inquilino antes de que corra el handler; el límite de tasa por IP frena a
// quien prueba enlaces (A3). Un envío que no existe responde igual que un token inválido (A2).
internal static class ConsultarSeguimientoEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/seguimiento/{token}", async (string token, HttpContext http,
                ConsultarSeguimientoHandler handler, CancellationToken ct) =>
            {
                var envioId = (Guid)http.Items[InquilinoDelToken.ClaveEnvio]!;
                return await handler.HandleAsync(envioId, ct) is { } seguimiento
                    ? Results.Ok(seguimiento)
                    : InquilinoDelToken.EnlaceNoValido();
            })
            .WithTags("Seguimiento")
            .AllowAnonymous()
            .RequireRateLimiting(LimitesSeguimiento.Politica)
            .AddEndpointFilter<InquilinoDelToken>();
    }
}
