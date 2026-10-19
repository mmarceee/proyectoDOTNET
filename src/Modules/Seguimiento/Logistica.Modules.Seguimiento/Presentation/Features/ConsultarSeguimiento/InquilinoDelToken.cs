using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Seguimiento.Presentation.Features.ConsultarSeguimiento;

// CU-60 (ADR-0002, sección 2.5): el seguimiento público no tiene sesión, así que el inquilino sale del token firmado.
// Este filtro valida la firma antes de que el endpoint toque cualquier dato y, si es válida, arma la identidad del
// request con el operador del token como claim, igual que la cookie o la clave de API: desde ahí ICurrentTenant y el
// filtro "Tenant" funcionan como siempre. Nunca se busca el envío sin filtro para "descubrir" su operador.
// Un token inválido responde lo mismo que un envío que no existe (A1 y A2): no da pistas para tantear.
internal sealed class InquilinoDelToken(TokenSeguimiento tokens) : IEndpointFilter
{
    // El envío del token, para el endpoint.
    public const string ClaveEnvio = "Seguimiento.EnvioId";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var http = contexto.HttpContext;
        if (!tokens.TryLeer(http.GetRouteValue("token") as string, out var operadorId, out var envioId))
        {
            return EnlaceNoValido();
        }

        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimsDeSesion.OperadorId, operadorId.ToString())], authenticationType: "TokenSeguimiento"));
        http.Items[ClaveEnvio] = envioId;
        return await siguiente(contexto);
    }

    // A1 y A2: la misma respuesta, sin más detalle.
    public static IResult EnlaceNoValido() =>
        TypedResults.Problem(title: "Enlace no válido", statusCode: StatusCodes.Status404NotFound);
}
