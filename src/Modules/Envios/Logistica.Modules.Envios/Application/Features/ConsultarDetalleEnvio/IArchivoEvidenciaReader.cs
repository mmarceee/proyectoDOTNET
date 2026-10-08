namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal interface IArchivoEvidenciaReader
{
    Task<ArchivoEvidenciaDto?> ConsultarAsync(string numero, Guid archivoId, CancellationToken ct);
}

internal sealed record ArchivoEvidenciaDto(string TipoContenido, byte[] Contenido);
