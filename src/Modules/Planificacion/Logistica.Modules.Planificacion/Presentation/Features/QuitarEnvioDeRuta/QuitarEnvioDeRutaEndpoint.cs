using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.QuitarEnvioDeRuta;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.QuitarEnvioDeRuta;

internal static class QuitarEnvioDeRutaEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/rutas/{id:guid}/envios/{envioId:guid}", async (Guid id, Guid envioId,
            [FromBody] QuitarEnvioDeRutaRequest request, QuitarEnvioDeRutaHandler handler, CancellationToken ct)
            => Results.Ok(await handler.HandleAsync(new(id, envioId, request.RevisionEsperada), ct)));
    }
}
