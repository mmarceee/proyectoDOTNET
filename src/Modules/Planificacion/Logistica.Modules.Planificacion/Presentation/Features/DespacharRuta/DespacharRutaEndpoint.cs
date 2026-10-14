using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.DespacharRuta;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.DespacharRuta;

internal static class DespacharRutaEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/rutas/{id:guid}/despacho/prevalidacion", async (Guid id, DespacharRutaRequest request, DespacharRutaHandler handler, CancellationToken ct)
            => Results.Ok(await handler.PrevalidarAsync(new(id, request.RevisionEsperada), ct)));
        group.MapPost("/rutas/{id:guid}/despacho", async (Guid id, DespacharRutaRequest request, DespacharRutaHandler handler, CancellationToken ct)
            => Results.Ok(await handler.HandleAsync(new(id, request.RevisionEsperada), ct)));
    }
}
