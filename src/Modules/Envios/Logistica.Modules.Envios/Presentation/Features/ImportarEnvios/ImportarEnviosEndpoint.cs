using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.ImportarEnvios;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.ImportarEnvios;

// CU-11. Endpoints delgados: leen el archivo, arman el command y llaman al handler. La vista previa no guarda nada;
// confirmar recibe otra vez el archivo y vuelve a validar cada envío, porque la API no guarda estado entre requests.
internal static class ImportarEnviosEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/importacion/vista-previa",
                (ImportarEnviosRequest request, ImportarEnviosHandler handler, CancellationToken ct) =>
                    ProcesarAsync(request, handler, confirmar: false, ct))
            .RequireAuthorization(Politicas.Comercio);
        group.MapPost("/importacion",
                (ImportarEnviosRequest request, ImportarEnviosHandler handler, CancellationToken ct) =>
                    ProcesarAsync(request, handler, confirmar: true, ct))
            .RequireAuthorization(Politicas.Comercio);
    }

    private static async Task<Ok<ImportarEnviosResponse>> ProcesarAsync(
        ImportarEnviosRequest request, ImportarEnviosHandler handler, bool confirmar, CancellationToken ct)
    {
        var filas = LectorArchivoEnvios.Leer(request.Archivo);
        var envios = await handler.HandleAsync(new ImportarEnviosCommand(filas, confirmar), ct);

        return TypedResults.Ok(new ImportarEnviosResponse(
            envios.Select(e => new EnvioImportadoResponse(
                e.Referencia, e.Filas, e.Resultado.ToString(), e.Numero, e.Tarifa, e.Detalle)).ToList(),
            PuedeConfirmar: envios.Any(e => e.Resultado == ResultadoImportacion.ACrear)));
    }
}
