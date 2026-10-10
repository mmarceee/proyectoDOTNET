using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.ConsultarEnviosDisponibles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.ConsultarEnviosDisponibles;

internal static class ConsultarEnviosDisponiblesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/envios-disponibles", async (DateOnly fecha, Guid? zonaId, Guid? franjaId, string? texto,
            int? pagina, int? tamanoPagina, ConsultarEnviosDisponiblesHandler service, CancellationToken ct)
            => Results.Ok(await service.DisponiblesAsync(fecha, zonaId, franjaId, texto, pagina ?? 1, tamanoPagina ?? 20, ct)));

    }
}
