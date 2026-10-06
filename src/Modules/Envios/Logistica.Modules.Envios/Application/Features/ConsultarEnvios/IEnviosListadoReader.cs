namespace Logistica.Modules.Envios.Application.Features.ConsultarEnvios;

internal interface IEnviosListadoReader
{
    Task<ConsultarEnviosResult> ConsultarAsync(
        ConsultarEnviosQuery query,
        CancellationToken ct);
}
