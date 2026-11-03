using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.ConsultarEnviosApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.ConsultarEnviosApi;

internal static class ConsultarEnviosApiEndpoints
{
    public static void Map(RouteGroupBuilder version, string sufijo)
    {
        version.MapGet("/envios", ListarAsync).WithName("ListarEnvios" + sufijo)
            .WithSummary("Listar envíos")
            .WithDescription("Devuelve una página de la relación y ambiente de la clave, del más reciente al más antiguo.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        version.MapGet("/envios/{numero}", ObtenerAsync).WithName("ObtenerEnvio" + sufijo)
            .WithSummary("Consultar un envío y su historial")
            .WithDescription("Devuelve el resumen, los códigos de bulto y el historial cronológico. El progreso se basa en los resultados registrados por el modelo actual de Envíos.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<Ok<PaginaEnviosApiResponse>> ListarAsync(HttpContext http, ConsultarEnviosApiHandler handler,
        CancellationToken ct, string? estado = null, DateOnly? fechaDesde = null, DateOnly? fechaHasta = null,
        string? referenciaComercio = null, int pagina = 1, int tamanoPagina = 20) =>
        TypedResults.Ok(await handler.ListarAsync(estado, fechaDesde, fechaHasta, referenciaComercio, pagina, tamanoPagina,
            EsPrueba(http), ct));

    private static async Task<Results<Ok<DetalleEnvioApiResponse>, NotFound>> ObtenerAsync(
        string numero, HttpContext http, ConsultarEnviosApiHandler handler, CancellationToken ct) =>
        await handler.ObtenerAsync(numero, EsPrueba(http), ct) is { } envio
            ? TypedResults.Ok(envio) : TypedResults.NotFound();

    private static bool EsPrueba(HttpContext http) => http.User.FindFirst(AccesoApiPublica.AmbienteClaim)?.Value == "true";
}
