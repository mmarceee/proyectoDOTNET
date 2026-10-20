namespace Logistica.Modules.Seguimiento.Application.Abstractions;

internal sealed record MensajeAviso(Guid Id, string Tipo, DateTimeOffset FechaEvento,
    string NumeroEnvio, string Estado, bool Prueba);

internal sealed record ResultadoEnvioAviso(bool Exitosa, int? CodigoRespuesta, string? Error);

internal interface IEmisorAvisos
{
    Task<ResultadoEnvioAviso> EnviarAsync(string urlDestino, string secretoFirma, MensajeAviso mensaje, CancellationToken ct);
}
