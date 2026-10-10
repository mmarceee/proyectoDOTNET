using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.ConsultarRutas;

internal static class ConsultarRutasEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/rutas", async (DateOnly? fecha, ConsultarRutasHandler service, CancellationToken ct) => Results.Ok(await service.ListarAsync(fecha, ct)));
        group.MapGet("/rutas/{id:guid}", async (Guid id, ConsultarRutasHandler service, CancellationToken ct) => Results.Ok(await service.DetalleAsync(id, ct)));
        group.MapGet("/rutas/{id:guid}/validaciones", async (Guid id, int? pagina, ConsultarRutasHandler service, CancellationToken ct)
            => Results.Ok(await service.ValidacionesAsync(id, pagina ?? 1, ct)));
    }
}
