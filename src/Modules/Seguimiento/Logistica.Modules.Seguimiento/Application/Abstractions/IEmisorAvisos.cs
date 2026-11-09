using Logistica.Modules.Seguimiento.Domain.Avisos;

namespace Logistica.Modules.Seguimiento.Application.Abstractions;

// RF 36: Version es la de la suscripción. En v1 el contenido es el de siempre; en v2 lleva además la versión y el
// bulto del hecho (null en los hechos del envío).
internal sealed record MensajeAviso(Guid Id, string Tipo, DateTimeOffset FechaEvento,
    string NumeroEnvio, string Estado, bool Prueba, int Version = SuscripcionAviso.VersionInicial, BultoAviso? Bulto = null);

internal sealed record ResultadoEnvioAviso(bool Exitosa, int? CodigoRespuesta, string? Error);

internal interface IEmisorAvisos
{
    Task<ResultadoEnvioAviso> EnviarAsync(string urlDestino, string secretoFirma, MensajeAviso mensaje, CancellationToken ct);
}
