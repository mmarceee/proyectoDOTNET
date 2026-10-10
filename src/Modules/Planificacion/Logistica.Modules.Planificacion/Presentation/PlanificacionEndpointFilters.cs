using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Planificacion.Presentation;

internal static class PlanificacionEndpointFilters
{
    public static void Add(RouteGroupBuilder group)
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
    }
}
