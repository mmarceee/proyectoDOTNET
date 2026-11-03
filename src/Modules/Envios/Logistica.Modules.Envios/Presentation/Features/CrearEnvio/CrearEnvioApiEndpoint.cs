using System.Net.Mail;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.CrearEnvio;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Seguimiento.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.CrearEnvio;

internal static class CrearEnvioApiEndpoint
{
    public static void Map(RouteGroupBuilder version, string sufijo)
    {
        version.MapPost("/envios", CrearAsync).WithName("CrearEnvio" + sufijo)
            .WithSummary("Crear un envío")
            .WithDescription("Cotiza y admite un envío en el ambiente de la clave. La referencia comercial es obligatoria, tiene hasta 50 caracteres y distingue mayúsculas. Repetir una referencia de la misma relación y ambiente devuelve el envío existente.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    public static void MapSeguimientoPrueba(RouteGroupBuilder version) =>
        version.MapGet("/pruebas/envios/{numero}/seguimiento", SeguirPruebaAsync)
            .WithName("SeguirEnvioPruebaV1")
            .WithSummary("Consultar el enlace de seguimiento de un envío de prueba")
            .WithDescription("Devuelve el resultado del alta para una clave de prueba de la misma relación comercial.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

    private static async Task<Results<Created<AltaEnvioApiResponse>, Ok<AltaEnvioApiResponse>, ProblemHttpResult>> CrearAsync(
        CrearEnvioApiRequest request, HttpContext http, CrearEnvioHandler handler,
        ISeguimientoModuleApi seguimiento, CancellationToken ct)
    {
        var referencia = Envio.NormalizarReferencia(request.ReferenciaComercio)
            ?? throw new DomainException("La referencia del comercio es obligatoria.");
        if (request.Bultos.Any(b => b is null))
            throw new DomainException("Cada bulto debe tener peso y medidas.");
        if (request.Destinatario.Email is { } email
            && (!MailAddress.TryCreate(email, out var direccion) || direccion.Address != email))
            throw new DomainException("El correo del destinatario no es válido.");

        var command = CrearEnvioEndpoint.AComando(
            new CrearEnvioRequest(request.Destinatario, request.Direccion, request.Bultos, request.Modalidad),
            OrigenEvento.Api, referencia, EsPrueba(http));

        try
        {
            var alta = await handler.CrearOObtenerAsync(command, ct);
            var enlace = alta.Envio.EsPrueba
                ? EnlacePrueba(alta.Envio.Numero)
                : seguimiento.EnlaceDe(alta.Envio.Id);
            var respuesta = Respuesta(alta.Envio, enlace);
            return alta.Creado ? TypedResults.Created(enlace, respuesta) : TypedResults.Ok(respuesta);
        }
        catch (EnvioNoCotizableException ex)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "El envío no se puede cotizar.", detail: ex.Message);
        }
    }

    private static async Task<Results<Ok<AltaEnvioApiResponse>, NotFound>> SeguirPruebaAsync(
        string numero, HttpContext http, IEnvioRepository envios, CancellationToken ct)
    {
        if (!EsPrueba(http) || await envios.ObtenerPruebaPorNumeroAsync(numero, ct) is not { } envio)
            return TypedResults.NotFound();
        return TypedResults.Ok(Respuesta(envio, EnlacePrueba(numero)));
    }

    private static bool EsPrueba(HttpContext http) => http.User.FindFirst(AccesoApiPublica.AmbienteClaim)?.Value == "true";

    private static string EnlacePrueba(string numero) => $"/api/v1/pruebas/envios/{numero}/seguimiento";

    // El estado del alta es Creado. La evolución se consulta por seguimiento.
    private static AltaEnvioApiResponse Respuesta(Envio envio, string enlace) =>
        new(envio.Numero, envio.ReferenciaComercio!, envio.MontoTarifa, "Creado", enlace);
}
