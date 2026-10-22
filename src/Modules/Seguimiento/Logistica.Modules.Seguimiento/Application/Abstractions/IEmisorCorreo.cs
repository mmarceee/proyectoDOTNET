namespace Logistica.Modules.Seguimiento.Application.Abstractions;

// La marca con que se presenta el correo: la del operador (CU-07) o la neutra.
internal sealed record MarcaCorreo(string Nombre, string ColorPrimario, string? EmailContacto, string? TelefonoContacto);

// Id: el de la notificación; el emisor lo usa como Message-ID, así un reenvío del mismo correo se reconoce.
internal sealed record MensajeCorreo(Guid Id, string Para, string? NombrePara, string Asunto, string Titulo, string Texto,
    string Enlace, MarcaCorreo Marca);

internal sealed record ResultadoEnvioCorreo(bool Exitoso, string? Error);

internal interface IEmisorCorreo
{
    Task<ResultadoEnvioCorreo> EnviarAsync(MensajeCorreo mensaje, CancellationToken ct);
}
