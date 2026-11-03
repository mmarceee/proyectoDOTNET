using Logistica.Http.Contracts.ClavesApi;
using Logistica.Modules.Administracion.Application.Features.GestionarClavesApi;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarClavesApi;

internal static class ClavesApiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/api/portal/claves-api").WithTags("Claves de API")
            .RequireAuthorization(Politicas.Comercio)
            .RequireAuthorization(p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser().RequireClaim(ClaimsDeSesion.ComercioId));

        grupo.AddEndpointFilter(async (contexto, siguiente) =>
        {
            var http = contexto.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsPost(http.Request.Method))
            {
                try
                {
                    await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.Problem(statusCode: 400, title: "La solicitud no tiene un token válido.");
                }
            }

            return await siguiente(contexto);
        });

        grupo.MapGet("/antifalsificacion", (HttpContext http, IAntiforgery antiforgery) =>
            TypedResults.Ok(new TokenClaveApiResponse(antiforgery.GetAndStoreTokens(http).RequestToken!)));
        grupo.MapGet("", async (GestionarClavesApiHandler handler, CancellationToken ct) =>
            TypedResults.Ok((await handler.ListarAsync(ct)).Select(Respuesta).ToArray()));
        grupo.MapPost("", CrearAsync);
        grupo.MapPost("/{id:guid}/revocacion", RevocarAsync);
    }

    private static async Task<Created<ClaveApiCreadaResponse>> CrearAsync(
        CrearClaveApiRequest request, GestionarClavesApiHandler handler, CancellationToken ct)
    {
        var tipo = request.Tipo switch
        {
            "Produccion" => TipoClaveApi.Produccion,
            "Prueba" => TipoClaveApi.Prueba,
            _ => throw new DomainException("Elegí una clave de producción o de prueba."),
        };
        var (clave, secreto) = await handler.CrearAsync(request.Nombre, tipo, ct);
        return TypedResults.Created((string?)null, new ClaveApiCreadaResponse(Respuesta(clave), secreto));
    }

    private static async Task<Results<NoContent, NotFound>> RevocarAsync(
        Guid id, GestionarClavesApiHandler handler, CancellationToken ct) =>
        await handler.RevocarAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static ClaveApiResponse Respuesta(ClaveApi clave) =>
        new(clave.Id, clave.Nombre, clave.Tipo.ToString(), clave.Prefijo,
            clave.CreadaEn, clave.UltimoUsoEn, clave.RevocadaEn);
}
