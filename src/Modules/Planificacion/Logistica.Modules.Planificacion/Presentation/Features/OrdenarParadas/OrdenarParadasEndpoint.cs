using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.OrdenarParadas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.OrdenarParadas;

internal static class OrdenarParadasEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/rutas/{id:guid}/orden", async (Guid id, OrdenarParadasHandler handler, CancellationToken ct)
            => Results.Ok(await handler.ConsultarAsync(id, ct)));
        group.MapPost("/rutas/{id:guid}/orden/propuesta", async (Guid id, ProponerOrdenParadasRequest request,
            OrdenarParadasHandler handler, CancellationToken ct)
            => Results.Ok(await handler.ProponerAsync(id, request.RevisionEsperada, ct)));
        group.MapPost("/rutas/{id:guid}/orden/prevalidacion", async (Guid id, RevisarOrdenParadasRequest request,
            OrdenarParadasHandler handler, CancellationToken ct)
            => Results.Ok(await handler.RevisarAsync(id, request.RevisionEsperada, request.ParadaIds, ct)));
        group.MapPost("/rutas/{id:guid}/orden", async (Guid id, GuardarOrdenParadasRequest request,
            OrdenarParadasHandler handler, CancellationToken ct)
            => Results.Ok(await handler.GuardarAsync(id, request.RevisionEsperada, request.ParadaIds, request.ConfirmarAdvertencias, ct)));
    }
}
