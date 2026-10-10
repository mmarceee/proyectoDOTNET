using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.ArmarRuta;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.ArmarRuta;

internal static class ArmarRutaEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/rutas/prevalidacion", async (PrevalidarRutaRequest request, ArmarRutaHandler service, CancellationToken ct)
            => Results.Ok(await service.PrevalidarAsync(new(request.Fecha, request.RepartidorId, request.VehiculoId, request.EnvioIds, request.RutaId, request.RevisionEsperada), ct)));
        group.MapPost("/rutas", async (CrearRutaRequest request, ArmarRutaHandler service, CancellationToken ct) =>
        {
            var ruta = await service.CrearAsync(new(request.Fecha, request.RepartidorId, request.VehiculoId, request.EnvioIds), ct);
            return Results.Created($"/api/planificacion/rutas/{ruta.Id}", ruta);
        });
        group.MapPost("/rutas/{id:guid}/envios", async (Guid id, AgregarEnviosRutaRequest request, ArmarRutaHandler service, CancellationToken ct)
            => Results.Ok(await service.AgregarAsync(id, new(request.RevisionEsperada, request.EnvioIds), ct)));
        group.MapPut("/rutas/{id:guid}/planificacion", async (Guid id, ModificarRutaRequest request, ArmarRutaHandler service, CancellationToken ct)
            => Results.Ok(await service.ModificarAsync(id, new(request.RevisionEsperada, request.Fecha, request.RepartidorId, request.VehiculoId), ct)));

    }
}
