using Logistica.Http.Contracts.Avisos;
using Logistica.Modules.Seguimiento.Application.Features.HistorialAvisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Seguimiento.Presentation.Features.HistorialAvisos;

internal static class HistorialAvisosEndpoints
{
    private const string Ruta = "/api/avisos/entregas";
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var grupo = AccesoAvisos.CrearGrupo(endpoints, Ruta);
        grupo.MapGet("", async (string? estado, DateOnly? desde, DateOnly? hasta, int? pagina,
            ConsultarHistorialAvisosHandler handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.ListarAsync(estado, desde, hasta, pagina ?? 1, ct)));
        grupo.MapGet("/{id:guid}", ObtenerAsync);
        grupo.MapPost("/{id:guid}/reenvio", ReenviarAsync);
        grupo.MapPost("/reenvios", ReenviarLoteAsync);
    }

    private static async Task<Results<Ok<DetalleEntregaAvisoResponse>, NotFound>> ObtenerAsync(
        Guid id, ConsultarHistorialAvisosHandler handler, CancellationToken ct) =>
        await handler.ObtenerAsync(id, ct) is { } detalle ? TypedResults.Ok(detalle) : TypedResults.NotFound();

    private static async Task<Results<Accepted<ReenvioAvisosResponse>, NotFound>> ReenviarAsync(
        Guid id, ReenviarAvisosHandler handler, CancellationToken ct) =>
        await handler.HandleAsync([id], ct)
            ? TypedResults.Accepted($"{Ruta}/{id}", new ReenvioAvisosResponse(1, "Reenvío solicitado. El aviso está pendiente de procesamiento."))
            : TypedResults.NotFound();

    private static async Task<Results<Accepted<ReenvioAvisosResponse>, NotFound>> ReenviarLoteAsync(
        ReenviarAvisosRequest request, ReenviarAvisosHandler handler, CancellationToken ct) =>
        await handler.HandleAsync(request.EntregaIds, ct)
            ? TypedResults.Accepted(Ruta, new ReenvioAvisosResponse(request.EntregaIds.Distinct().Count(), "Reenvíos solicitados. Los avisos están pendientes de procesamiento."))
            : TypedResults.NotFound();
}
