using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Envios.Application.Features.CrearEnvio;
using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.CrearEnvio;

// POST /api/envios. Endpoint delgado: traduce HTTP al command y llama al handler.
internal static class CrearEnvioEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", CrearAsync);
    }

    private static async Task<Created<CrearEnvioResponse>> CrearAsync(
        CrearEnvioRequest request, CrearEnvioHandler handler, CancellationToken ct)
    {
        var d = request.Destinatario;
        var dir = request.Direccion;

        var command = new CrearEnvioCommand(
            new Destinatario(d.Nombre, d.Telefono, d.Email, d.Documento),
            new Direccion(dir.Calle, dir.Numero, dir.Localidad, dir.Departamento, dir.CodigoPostal, dir.Referencia),
            request.Bultos.Select(b => new BultoACrear(b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToList(),
            OrigenEvento.PortalComercio);

        var envio = await handler.HandleAsync(command, ct);

        return TypedResults.Created($"/api/envios/{envio.Id}", new CrearEnvioResponse(envio.Id, envio.Numero));
    }
}
