using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.CrearEnvio;
using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Presentation.Features.CrearEnvio;

// POST /api/envios. Endpoint delgado: traduce HTTP al command y llama al handler.
internal static class CrearEnvioEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // CU-10: lo crea el usuario de comercio, con el operador de la sesión elegido.
        group.MapPost("/", CrearAsync).RequireAuthorization(Politicas.Comercio);

        // Las franjas que el comercio puede elegir para la dirección y el día (CU-10, compromiso de entrega).
        group.MapGet("/franjas", FranjasAsync).RequireAuthorization(Politicas.Comercio);
    }

    private static async Task<Ok<IReadOnlyList<FranjaDisponibleResponse>>> FranjasAsync(
        string codigoPostal, DateOnly fecha, CrearEnvioHandler handler, CancellationToken ct)
    {
        var franjas = await handler.FranjasDisponiblesAsync(codigoPostal, fecha, ct);
        return TypedResults.Ok<IReadOnlyList<FranjaDisponibleResponse>>(
            franjas.Select(f => new FranjaDisponibleResponse(f.Id, f.Desde, f.Hasta)).ToList());
    }

    private static async Task<Created<CrearEnvioResponse>> CrearAsync(
        CrearEnvioRequest request, CrearEnvioHandler handler, CancellationToken ct)
    {
        var envio = await handler.HandleAsync(AComando(request, OrigenEvento.PortalComercio), ct);
        return TypedResults.Created($"/api/envios/{envio.Id}", new CrearEnvioResponse(envio.Id, envio.Numero));
    }

    internal static CrearEnvioCommand AComando(CrearEnvioRequest request, OrigenEvento origen,
        string? referenciaComercio = null, bool esPrueba = false)
    {
        var d = request.Destinatario;
        var dir = request.Direccion;
        var modalidad = request.Modalidad switch
        {
            "Estandar" => Modalidad.Estandar,
            "Urgente" => Modalidad.Urgente,
            _ => throw new DomainException("Elegí una modalidad válida: Estándar o Urgente."),
        };

        return new CrearEnvioCommand(
            new Destinatario(d.Nombre, d.Telefono, d.Email, d.Documento),
            new Direccion(dir.Calle, dir.Numero, dir.Localidad, dir.Departamento, dir.CodigoPostal, dir.Referencia),
            request.Bultos.Select(b => new BultoACrear(b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToList(),
            origen, modalidad, referenciaComercio, esPrueba, request.FechaEntrega, request.FranjaHorariaId);
    }
}
