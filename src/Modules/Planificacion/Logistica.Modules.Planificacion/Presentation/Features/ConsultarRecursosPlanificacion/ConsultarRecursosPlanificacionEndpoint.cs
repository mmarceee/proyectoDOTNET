using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRecursosPlanificacion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.ConsultarRecursosPlanificacion;

internal static class ConsultarRecursosPlanificacionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/recursos", async (DateOnly fecha, ConsultarRecursosPlanificacionHandler service, CancellationToken ct) => Results.Ok(await service.RecursosAsync(fecha, ct)));

    }
}
