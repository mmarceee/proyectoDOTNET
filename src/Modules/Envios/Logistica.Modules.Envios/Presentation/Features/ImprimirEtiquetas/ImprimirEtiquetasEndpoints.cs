using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.ImprimirEtiquetas;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.ImprimirEtiquetas;

internal static class ImprimirEtiquetasEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/portal/{numero}/etiquetas", async (string numero, ImprimirEtiquetasHandler handler,
            ICurrentTenant tenant, HttpContext http, CancellationToken ct) =>
            tenant.ComercioId is null ? Results.Forbid() : await Descargar([numero], handler, http, ct))
            .RequireAuthorization(Politicas.Comercio);
        group.MapPost("/portal/etiquetas", async (ImprimirEtiquetasRequest request, ImprimirEtiquetasHandler handler,
            ICurrentTenant tenant, HttpContext http, CancellationToken ct) =>
            tenant.ComercioId is null ? Results.Forbid() : await Descargar(request.Numeros, handler, http, ct))
            .RequireAuthorization(Politicas.Comercio);
        group.MapGet("/{numero}/etiquetas", async (string numero, ImprimirEtiquetasHandler handler,
            HttpContext http, CancellationToken ct) => await Descargar([numero], handler, http, ct))
            .RequireAuthorization(Politicas.ImprimirEtiquetasDeposito);
    }

    private static async Task<IResult> Descargar(IReadOnlyList<string>? numeros, ImprimirEtiquetasHandler handler,
        HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        var resultado = await handler.HandleAsync(numeros, ct);
        if (resultado.NoEncontrado) return Results.NotFound(new { mensaje = "Envío no encontrado." });
        if (resultado.Error is { } error) return Results.BadRequest(new { mensaje = error });
        return Results.File(resultado.Pdf!, "application/pdf", "etiquetas.pdf");
    }
}
