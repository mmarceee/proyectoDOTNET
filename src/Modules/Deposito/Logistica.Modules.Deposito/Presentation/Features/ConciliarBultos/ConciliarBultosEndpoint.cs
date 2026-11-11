using System.Security.Claims;
using Logistica.Http.Contracts.Deposito;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.ConciliarBultos;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Deposito.Presentation.Features.ConciliarBultos;

internal static class ConciliarBultosEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        var conciliacion = group.MapGroup("/custodia").RequireAuthorization(Politicas.OperarioDeposito);
        conciliacion.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.Request.Method == "POST" && context.HttpContext.Request.Headers.ContainsKey("Cookie"))
            {
                try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
                catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            }
            try { return await next(context); }
            catch (DomainException e) { return Results.Problem(statusCode: 409, title: e.Message); }
        });
        conciliacion.MapGet("/{envioId:guid}", async (Guid envioId, IEnviosModuleApi envios,
            IPlanificacionModuleApi rutas, CancellationToken ct) =>
        {
            var bultos = await envios.ConsultarCustodiaAsync([envioId], ct);
            if (bultos.Count == 0) return Results.NotFound();
            var activas = await rutas.ConsultarReservasActivasAsync(bultos.Select(b => b.BultoId).ToArray(), ct);
            var revisiones = new Dictionary<Guid, long?>();
            foreach (var ruta in activas.Select(r => r.RutaId).Distinct()) revisiones[ruta] = await rutas.RevisionAsync(ruta, ct);
            return Results.Ok(bultos.Select(b =>
            {
                var reserva = activas.SingleOrDefault(r => r.BultoId == b.BultoId);
                return new CustodiaBultoResponse(b.BultoId, b.EnvioId, b.Codigo, b.Estado, b.Ubicacion,
                    b.Revision, reserva?.RutaId, reserva is null ? null : revisiones[reserva.RutaId]);
            }).ToArray());
        });
        conciliacion.MapPost("/conciliaciones", async (ConciliarBultosRequest request, HttpContext http,
            ConciliarBultosHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
            var pedido = new ConciliacionBultos(request.OperacionId, request.RutaId, request.RevisionRuta,
                request.Bultos?.Select(b => new ConfirmacionCustodiaBulto(b.BultoId, b.RevisionEsperada)).ToArray()!,
                request.EnDeposito, request.Motivo, actor);
            return Results.Ok(new { Bultos = await handler.HandleAsync(pedido, ct) });
        });
    }
}
