using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using MimeKit;

namespace Logistica.Modules.Seguimiento.Infrastructure.Correo;

// El correo al destinatario con la marca del operador (CU-62, paso 4): texto plano y HTML con estilos en línea, que es
// lo que respetan los clientes de correo. Todo dato variable se codifica antes de entrar al HTML.
internal static partial class PlantillaCorreo
{
    private const string ColorPorDefecto = "#1F4E79";

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorHex();

    public static MimeMessage Armar(MensajeCorreo m, MailboxAddress remitente)
    {
        var correo = new MimeMessage();
        // La dirección es la del sistema; el nombre visible, el del operador.
        correo.From.Add(new MailboxAddress(m.Marca.Nombre, remitente.Address));
        if (m.Marca.EmailContacto is { } contacto && MailboxAddress.TryParse(contacto, out var responderA))
            correo.ReplyTo.Add(responderA);
        correo.To.Add(new MailboxAddress(m.NombrePara ?? "", m.Para));
        correo.Subject = m.Asunto;
        correo.MessageId = $"{m.Id:N}@logistica";
        correo.Body = new BodyBuilder { TextBody = Texto(m), HtmlBody = Html(m) }.ToMessageBody();
        return correo;
    }

    private static string Saludo(MensajeCorreo m) => m.NombrePara is { } nombre ? $"Hola, {nombre}:" : "Hola:";

    private static string Contacto(MarcaCorreo marca) =>
        string.Join(" · ", new[] { marca.EmailContacto, marca.TelefonoContacto }.Where(c => !string.IsNullOrWhiteSpace(c)));

    internal static string Texto(MensajeCorreo m)
    {
        var texto = new StringBuilder()
            .AppendLine(m.Titulo).AppendLine()
            .AppendLine(Saludo(m)).AppendLine(m.Texto).AppendLine()
            .AppendLine("Seguí tu envío en:").AppendLine(m.Enlace).AppendLine()
            .AppendLine(m.Marca.Nombre);
        if (Contacto(m.Marca) is { Length: > 0 } contacto) texto.AppendLine(contacto);
        return texto.ToString();
    }

    internal static string Html(MensajeCorreo m)
    {
        static string C(string? valor) => WebUtility.HtmlEncode(valor ?? "");
        var color = ColorHex().IsMatch(m.Marca.ColorPrimario) ? m.Marca.ColorPrimario : ColorPorDefecto;
        var contacto = Contacto(m.Marca);
        return $"""
            <!doctype html>
            <html lang="es"><body style="margin:0;padding:24px;background:#f4f5f7;font-family:Arial,Helvetica,sans-serif;color:#1f2933">
            <table role="presentation" width="100%" style="max-width:560px;margin:0 auto;background:#ffffff;border-collapse:collapse">
            <tr><td style="background:{color};color:#ffffff;padding:20px 24px;font-size:20px;font-weight:bold">{C(m.Marca.Nombre)}</td></tr>
            <tr><td style="padding:24px">
            <h1 style="margin:0 0 16px;font-size:20px">{C(m.Titulo)}</h1>
            <p style="margin:0 0 8px">{C(Saludo(m))}</p>
            <p style="margin:0 0 24px;line-height:1.5">{C(m.Texto)}</p>
            <p style="margin:0 0 24px"><a href="{C(m.Enlace)}" style="background:{color};color:#ffffff;padding:12px 20px;text-decoration:none;border-radius:4px;display:inline-block">Seguir mi envío</a></p>
            <p style="margin:0;font-size:12px;color:#52606d">Si el botón no funciona, copiá esta dirección en el navegador:<br>{C(m.Enlace)}</p>
            </td></tr>
            <tr><td style="padding:16px 24px;font-size:12px;color:#52606d;border-top:1px solid #e4e7eb">{C(m.Marca.Nombre)}{(contacto.Length > 0 ? " · " + C(contacto) : "")}</td></tr>
            </table></body></html>
            """;
    }
}
