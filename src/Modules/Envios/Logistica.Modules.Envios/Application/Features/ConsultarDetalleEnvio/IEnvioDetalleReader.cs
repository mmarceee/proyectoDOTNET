namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal interface IEnvioDetalleReader
{
    // Null significa que no existe un envío visible para el inquilino actual.
    Task<EnvioDetalleDto?> ConsultarAsync(
        ConsultarDetalleEnvioQuery query,
        CancellationToken ct);
}
