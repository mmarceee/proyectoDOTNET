using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Rutas;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation.Features.Rutas;

internal static class RutasEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.AddEndpointFilter<AccesoPlanificacion>();
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (RutaNoEncontradaException) { return Results.Problem(statusCode: 404, title: "La ruta no existe."); }
            catch (ConflictoRutaException e)
            { return Results.Problem(statusCode: 409, title: e.Message, extensions: new Dictionary<string, object?>
                { ["codigo"] = e.Codigo, ["envioIdsNoDisponibles"] = e.EnvioIds, ["recursosEnConflicto"] = e.Recursos, ["revisionActual"] = e.RevisionActual }); }
            catch (RestriccionesRutaException e)
            { return Results.Problem(statusCode: 400, title: "La ruta no cumple las restricciones.", detail: e.Message,
                extensions: new Dictionary<string, object?> { ["restricciones"] = e.Restricciones }); }
            catch (DomainException e) { return Results.Problem(statusCode: 400, title: "La solicitud no cumple una regla.", detail: e.Message); }
        });
        group.MapGet("/recursos", async (DateOnly fecha, PlanificacionService service, CancellationToken ct) => Results.Ok(await service.RecursosAsync(fecha, ct)));
        group.MapGet("/envios-disponibles", async (DateOnly fecha, Guid? zonaId, Guid? franjaId, string? texto,
            int? pagina, int? tamanoPagina, PlanificacionService service, CancellationToken ct)
            => Results.Ok(await service.DisponiblesAsync(fecha, zonaId, franjaId, texto, pagina ?? 1, tamanoPagina ?? 20, ct)));
        group.MapPost("/rutas/prevalidacion", async (PrevalidarRutaRequest request, PlanificacionService service, CancellationToken ct)
            => Results.Ok(await service.PrevalidarAsync(request, ct)));
        group.MapPost("/rutas", async (CrearRutaRequest request, PlanificacionService service, CancellationToken ct) =>
        {
            var ruta = await service.CrearAsync(request, ct);
            return Results.Created($"/api/planificacion/rutas/{ruta.Id}", ruta);
        });
        group.MapGet("/rutas", async (DateOnly? fecha, PlanificacionService service, CancellationToken ct) => Results.Ok(await service.ListarAsync(fecha, ct)));
        group.MapGet("/rutas/{id:guid}", async (Guid id, PlanificacionService service, CancellationToken ct) => Results.Ok(await service.DetalleAsync(id, ct)));
        group.MapPost("/rutas/{id:guid}/envios", async (Guid id, AgregarEnviosRutaRequest request, PlanificacionService service, CancellationToken ct)
            => Results.Ok(await service.AgregarAsync(id, request, ct)));
        group.MapPut("/rutas/{id:guid}/planificacion", async (Guid id, ModificarRutaRequest request, PlanificacionService service, CancellationToken ct)
            => Results.Ok(await service.ModificarAsync(id, request, ct)));
        group.MapGet("/rutas/{id:guid}/validaciones", async (Guid id, int? pagina, PlanificacionService service, CancellationToken ct)
            => Results.Ok(await service.ValidacionesAsync(id, pagina ?? 1, ct)));
    }
}
