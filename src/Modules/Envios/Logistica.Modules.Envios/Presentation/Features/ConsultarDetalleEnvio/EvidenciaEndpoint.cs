using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.ConsultarDetalleEnvio;

internal static class EvidenciaEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/backoffice/envios/{numero}/evidencias/{archivoId:guid}",
            async (string numero, Guid archivoId, IArchivoEvidenciaReader reader, HttpContext http, CancellationToken ct) =>
            {
                var archivo = await reader.ConsultarAsync(numero, archivoId, ct);
                if (archivo is null)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return Results.File(archivo.Contenido, archivo.TipoContenido);
            }).WithTags("Envios");
    }
}
