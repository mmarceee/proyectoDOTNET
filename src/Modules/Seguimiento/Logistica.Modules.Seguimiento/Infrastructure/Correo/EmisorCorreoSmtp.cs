using Logistica.Modules.Seguimiento.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Logistica.Modules.Seguimiento.Infrastructure.Correo;

// ConnectionStrings:Smtp, con la forma smtp://[usuario:contraseña@]host:puerto (STARTTLS cuando el servidor lo ofrece,
// como Mailpit en desarrollo o Brevo en el 587) o smtps://... (TLS desde el inicio). Seguimiento:Correo:Remitente es la
// dirección desde la que salen los correos. Se validan al enviar, así la API y el Worker arrancan aunque falten.
internal sealed class ServidorSmtp(string? conexion, string? remitente)
{
    private readonly Lazy<(Uri Uri, MailboxAddress Remitente)> _datos = new(() =>
    {
        if (!Uri.TryCreate(conexion, UriKind.Absolute, out var uri) || uri.Scheme is not ("smtp" or "smtps") || uri.Port <= 0)
            throw new InvalidOperationException("Configurá ConnectionStrings:Smtp como smtp://host:puerto o smtps://host:puerto.");
        if (!MailboxAddress.TryParse(remitente, out var direccion))
            throw new InvalidOperationException("Configurá Seguimiento:Correo:Remitente con la dirección que envía los correos.");
        return (uri, direccion);
    });

    public Uri Uri => _datos.Value.Uri;
    public MailboxAddress Remitente => _datos.Value.Remitente;
    public SecureSocketOptions Seguridad => Uri.Scheme == "smtps" ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTlsWhenAvailable;

    public (string Usuario, string Contrasena)? Credenciales =>
        Uri.UserInfo.Split(':', 2) is [{ Length: > 0 } usuario, var contrasena]
            ? (Uri.UnescapeDataString(usuario), Uri.UnescapeDataString(contrasena))
            : null;
}

// Un intento de envío por SMTP. No reintenta: los reintentos son los de la mensajería (ADR-0003, sección 2.6).
internal sealed class EmisorCorreoSmtp(ServidorSmtp servidor) : IEmisorCorreo
{
    public async Task<ResultadoEnvioCorreo> EnviarAsync(MensajeCorreo mensaje, CancellationToken ct)
    {
        var correo = PlantillaCorreo.Armar(mensaje, servidor.Remitente);
        using var cliente = new SmtpClient { Timeout = 10_000 };
        try
        {
            await cliente.ConnectAsync(servidor.Uri.Host, servidor.Uri.Port, servidor.Seguridad, ct);
            if (servidor.Credenciales is { } credenciales)
                await cliente.AuthenticateAsync(credenciales.Usuario, credenciales.Contrasena, ct);
            await cliente.SendAsync(correo, ct);
            await cliente.DisconnectAsync(quit: true, ct);
            return new(true, null);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Sin el texto de la excepción: puede incluir direcciones de correo, y el error queda guardado.
            var codigo = e is SmtpCommandException smtp ? $" (código {(int)smtp.StatusCode})" : "";
            return new(false, $"El servidor de correo no aceptó el mensaje: {e.GetType().Name}{codigo}.");
        }
    }
}
