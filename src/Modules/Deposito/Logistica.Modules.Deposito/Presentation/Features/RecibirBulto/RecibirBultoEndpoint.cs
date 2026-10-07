using Logistica.Http.Contracts.Deposito;
using Logistica.Modules.Deposito.Application.Features.RecibirBulto;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Deposito.Presentation.Features.RecibirBulto;

// POST /api/deposito/recepciones. Para lectores o dispositivos que hablen con la API, y para las pruebas;
// el operario usa la página del Backoffice, que llama al mismo handler.
internal static class RecibirBultoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/recepciones", RecibirAsync);
    }

    private static async Task<Ok<RecibirBultoResponse>> RecibirAsync(
        RecibirBultoRequest request, RecibirBultoHandler handler, CancellationToken ct)
    {
        var command = new RecibirBultoCommand(
            request.CodigoBulto,
            new Medidas(request.PesoKg, request.LargoCm, request.AnchoCm, request.AltoCm));

        var recepcion = await handler.HandleAsync(command, ct);

        return TypedResults.Ok(new RecibirBultoResponse(
            recepcion.NumeroEnvio,
            recepcion.Resultado.ToString(),
            recepcion.Discrepancia,
            recepcion.Bultos.Select(b => new BultoRecepcionResponse(b.Codigo, b.Recibido)).ToList(),
            recepcion.EnvioEnDeposito));
    }
}
