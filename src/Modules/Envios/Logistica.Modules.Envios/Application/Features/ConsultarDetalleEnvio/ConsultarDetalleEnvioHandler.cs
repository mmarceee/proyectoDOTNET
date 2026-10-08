namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed class ConsultarDetalleEnvioHandler(IEnvioDetalleReader reader)
{
    public Task<EnvioDetalleDto?> HandleAsync(
        ConsultarDetalleEnvioQuery query,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Numero))
        {
            throw new ArgumentException(
                "El número del envío es obligatorio.",
                nameof(query.Numero));
        }

        return reader.ConsultarAsync(query with { Numero = query.Numero.Trim() }, ct);
    }
}
