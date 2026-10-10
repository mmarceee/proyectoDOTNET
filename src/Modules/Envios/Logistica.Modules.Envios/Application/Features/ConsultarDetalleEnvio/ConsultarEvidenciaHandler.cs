namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed class ConsultarEvidenciaHandler(IArchivoEvidenciaReader reader)
{
    public Task<ArchivoEvidenciaDto?> HandleAsync(ConsultarEvidenciaQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Numero) || query.ArchivoId == Guid.Empty)
        {
            return Task.FromResult<ArchivoEvidenciaDto?>(null);
        }

        return reader.ConsultarAsync(query.Numero.Trim(), query.ArchivoId, ct);
    }
}
