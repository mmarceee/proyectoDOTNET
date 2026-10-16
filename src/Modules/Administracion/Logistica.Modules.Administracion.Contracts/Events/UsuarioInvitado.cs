namespace Logistica.Modules.Administracion.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): un usuario fue creado (CU-02, paso 7) o se le reenvió la invitación
// (A6). El Worker le manda el correo con el enlace para definir su contraseña. Viaja por el outbox de Administración;
// el sobre (MessageId, inquilino, fecha y traza) lo agrega la tabla outbox.
// El enlace es relativo (/cuenta/activar?...): el Worker le antepone la dirección pública del sitio. Vence a las 48 h
// y es de un solo uso.
public sealed record UsuarioInvitado(Guid UsuarioId, string Email, string Nombre, string Perfil, string Enlace, bool Reenvio)
{
    // Nombre versionado del evento; también es la routing key en RabbitMQ.
    public const string Tipo = "UsuarioInvitado.v1";
}
