namespace Logistica.Modules.Seguimiento.Application.Abstractions;

// La marca con que se presenta el correo: la del operador (CU-07) o la neutra.
internal sealed record MarcaCorreo(string Nombre, string ColorPrimario, string? EmailContacto, string? TelefonoContacto);

// Id: el de la notificación; el emisor lo usa como Message-ID, así un reenvío del mismo correo se reconoce.
// Boton y PresentaEnlace: el texto del botón y el que precede al enlace en la versión de texto plano. Por defecto, los
// del seguimiento (CU-62); la invitación (CU-02) usa los suyos.
internal sealed record MensajeCorreo(Guid Id, string Para, string? NombrePara, string Asunto, string Titulo, string Texto,
    string Enlace, MarcaCorreo Marca, string Boton = "Seguir mi envío", string PresentaEnlace = "Seguí tu envío en:");

internal sealed record ResultadoEnvioCorreo(bool Exitoso, string? Error);

internal interface IEmisorCorreo
{
    Task<ResultadoEnvioCorreo> EnviarAsync(MensajeCorreo mensaje, CancellationToken ct);
}
