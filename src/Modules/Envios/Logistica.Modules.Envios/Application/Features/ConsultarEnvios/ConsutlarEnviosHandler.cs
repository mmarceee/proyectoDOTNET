namespace Logistica.Modules.Envios.Application.Features.ConsultarEnvios;

internal sealed class ConsultarEnviosHandler(IEnviosListadoReader reader)
{
    public Task<ConsultarEnviosResult> HandleAsync(
        ConsultarEnviosQuery query,
        CancellationToken ct)
    {
        if (query.Pagina < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query.Pagina),
                "La página debe ser mayor que cero.");
        }

        if (query.TamanoPagina < 1 || query.TamanoPagina > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query.TamanoPagina),
                "El tamaño de página debe estar entre 1 y 100.");
        }

        return reader.ConsultarAsync(query, ct);
    }
}
