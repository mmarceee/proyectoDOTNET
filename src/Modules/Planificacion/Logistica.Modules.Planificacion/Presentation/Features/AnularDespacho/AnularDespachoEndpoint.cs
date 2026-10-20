using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.AnularDespacho;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.AnularDespacho;

internal static class AnularDespachoEndpoint
{
    public static void Map(RouteGroupBuilder group)
        => group.MapPost("/rutas/{id:guid}/despacho/anulacion", async (Guid id, AnularDespachoRequest request,
            AnularDespachoHandler handler, CancellationToken ct)
            => Results.Ok(await handler.HandleAsync(new(id, request.RevisionEsperada, request.Motivo,
                request.ConfirmoSinCargaPendiente), ct)));
}
