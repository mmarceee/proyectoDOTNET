using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.EnviarInvitacion;

// Lo que trae el evento UsuarioInvitado.v1 de Administración (CU-02, paso 7 y A6). MensajeId identifica el correo: un
// reenvío del mismo mensaje lleva el mismo Message-ID.
internal sealed record InvitacionRecibida(Guid MensajeId, string Email, string Nombre, string Perfil, string Enlace,
    bool Reenvio);

// El servidor de correo no aceptó la invitación: la mensajería la reintenta o la pasa a fallidos (ADR-0003, sección 2.6).
internal sealed class InvitacionNoEnviadaException(string? error)
    : Exception($"La invitación no se pudo enviar. {error}");

// CU-02, paso 7: el correo con el enlace para que el usuario defina su contraseña, con la marca de su operador (CU-07).
internal sealed class EnviarInvitacionHandler(IIdentidadesOperadorRepository identidades, IEmisorCorreo emisor,
    DireccionPublica direccion, ICurrentTenant tenant)
{
    public async Task HandleAsync(InvitacionRecibida invitacion, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("El mensaje no tiene operador.");
        var identidad = await identidades.ObtenerAsync(operadorId, ct);
        var marca = identidad is null ? NotificarDestinatarioHandler.MarcaNeutra
            : new MarcaCorreo(identidad.Nombre, identidad.ColorPrimario, identidad.EmailContacto, identidad.TelefonoContacto);

        // El enlace viene relativo (/cuenta/activar?...): se le antepone la dirección pública del sitio.
        var correo = new MensajeCorreo(invitacion.MensajeId, invitacion.Email, invitacion.Nombre,
            invitacion.Reenvio ? $"Nuevo enlace para activar tu cuenta en {marca.Nombre}" : $"Te invitaron a {marca.Nombre}",
            "Activá tu cuenta",
            $"Te dieron acceso con el perfil {invitacion.Perfil}. Entrá al enlace para definir tu contraseña. " +
            "Vence a las 48 horas y se puede usar una sola vez.",
            direccion.Absoluta(invitacion.Enlace), marca, "Definir mi contraseña", "Definí tu contraseña en:");

        var resultado = await emisor.EnviarAsync(correo, ct);
        if (!resultado.Exitoso) throw new InvitacionNoEnviadaException(resultado.Error);
    }
}
