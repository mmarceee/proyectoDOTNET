using Logistica.Http.Contracts.Avisos;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Seguimiento.Presentation.Features.ConfigurarSuscripcionesAviso;

internal static class ConfigurarSuscripcionesAvisoEndpoints
{
    private const string Ruta = "/api/avisos/suscripciones";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup(Ruta).WithTags("Avisos")
            .RequireAuthorization(Politicas.Comercio)
            .RequireAuthorization(p => p.RequireAuthenticatedUser().RequireClaim(ClaimsDeSesion.ComercioId));
        grupo.AddEndpointFilter(async (contexto, siguiente) =>
        {
            var http = contexto.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsPost(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException)
                {
                    return Results.Problem(statusCode: 400, title: "La solicitud no tiene un token válido.");
                }
            }
            return await siguiente(contexto);
        });

        grupo.MapGet("/antifalsificacion", (HttpContext http, IAntiforgery antiforgery) =>
            TypedResults.Ok(new TokenAntifalsificacionResponse(antiforgery.GetAndStoreTokens(http).RequestToken!)));
        grupo.MapGet("", ListarAsync);
        grupo.MapGet("/{id:guid}", ObtenerAsync);
        grupo.MapPost("", CrearAsync);
        grupo.MapPost("/{id:guid}/desactivacion", DesactivarAsync);
        grupo.MapPost("/{id:guid}/secreto", RegenerarAsync);
        grupo.MapPost("/{id:guid}/prueba", EnviarPruebaAsync);
    }

    private static async Task<Ok<IReadOnlyList<SuscripcionAvisoResponse>>> ListarAsync(
        ConsultarSuscripcionesAvisoHandler handler, CancellationToken ct) =>
        TypedResults.Ok<IReadOnlyList<SuscripcionAvisoResponse>>((await handler.ListarAsync(ct)).Select(Respuesta).ToArray());

    private static async Task<Results<Ok<SuscripcionAvisoResponse>, NotFound>> ObtenerAsync(
        Guid id, ConsultarSuscripcionesAvisoHandler handler, CancellationToken ct) =>
        await handler.ObtenerAsync(id, ct) is { } vista ? TypedResults.Ok(Respuesta(vista)) : TypedResults.NotFound();

    private static async Task<Created<SecretoSuscripcionAvisoResponse>> CrearAsync(
        CrearSuscripcionAvisoRequest request, CrearSuscripcionAvisoHandler handler, CancellationToken ct)
    {
        var tipos = request.TiposEvento.Select(TipoEvento).ToArray();
        var resultado = await handler.HandleAsync(new(request.UrlDestino, tipos), ct);
        return TypedResults.Created($"{Ruta}/{resultado.Id}", new SecretoSuscripcionAvisoResponse(resultado.Id, resultado.SecretoFirma));
    }

    private static async Task<Results<NoContent, NotFound>> DesactivarAsync(
        Guid id, DesactivarSuscripcionAvisoHandler handler, CancellationToken ct) =>
        await handler.HandleAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<Ok<SecretoSuscripcionAvisoResponse>, NotFound>> RegenerarAsync(
        Guid id, RegenerarSecretoAvisoHandler handler, CancellationToken ct) =>
        await handler.HandleAsync(id, ct) is { } resultado
            ? TypedResults.Ok(new SecretoSuscripcionAvisoResponse(resultado.Id, resultado.SecretoFirma))
            : TypedResults.NotFound();

    private static SuscripcionAvisoResponse Respuesta(SuscripcionAvisoVista s) =>
        new(s.Id, s.UrlDestino, s.TiposEvento.Select(t => t.ToString()).ToArray(), s.Activa);

    private static async Task<Results<Ok<ResultadoPruebaAvisoResponse>, NotFound>> EnviarPruebaAsync(
        Guid id, EnviarPruebaAvisoHandler handler, CancellationToken ct) =>
        await handler.HandleAsync(id, ct) is { } resultado
            ? TypedResults.Ok(new ResultadoPruebaAvisoResponse(resultado.MensajeId, resultado.Exitosa,
                resultado.CodigoRespuesta, resultado.Error))
            : TypedResults.NotFound();

    private static TipoEventoAviso TipoEvento(string valor) =>
        Enum.TryParse<TipoEventoAviso>(valor, out var tipo) && Enum.IsDefined(tipo) && valor == tipo.ToString()
            ? tipo
            : throw new DomainException("Los tipos de evento válidos son CambioDeEstado, EntregaFallida y Devolucion.");
}
