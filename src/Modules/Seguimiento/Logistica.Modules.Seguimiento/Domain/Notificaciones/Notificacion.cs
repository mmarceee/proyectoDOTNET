using System.Net.Mail;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Notificaciones;

// Sólo correo: SMS y WhatsApp quedan fuera de alcance.
internal enum CanalNotificacion
{
    Email
}

internal enum TipoNotificacion
{
    CambioDeEstado,
    VentanaEstimada,
    SolicitudDeReprogramacion
}

// Pendiente: sin enviar todavía, o falló y quedan reintentos. Fallida: se agotaron los reintentos (CU-09 la reintenta).
// NoEnviable: el destinatario no tiene correo; no se reintenta (CU-62, A2).
internal enum EstadoNotificacion
{
    Pendiente,
    Enviada,
    Fallida,
    NoEnviable
}

// RF 27: la notificación al destinatario de un hecho de su envío (CU-62). Una por evento: el mismo evento procesado dos
// veces no manda dos correos.
internal sealed class Notificacion : Entity, IOperadorOwned, IComercioOwned
{
    public const int LargoError = 500;

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid EnvioId { get; private set; }
    // MessageId del evento de Envíos que originó la notificación.
    public Guid EventoId { get; private set; }
    public CanalNotificacion Canal { get; private set; }
    public TipoNotificacion Tipo { get; private set; }
    public string EstadoEnvio { get; private set; } = "";
    // Copia del correo del destinatario al crear la notificación: una foto del dato, no una relación.
    public string? Destino { get; private set; }
    public EstadoNotificacion Estado { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }
    public DateTimeOffset? EnviadaEn { get; private set; }
    public int Intentos { get; private set; }
    public string? UltimoError { get; private set; }

    public bool PorEnviar => Estado is EstadoNotificacion.Pendiente or EstadoNotificacion.Fallida;

    private Notificacion() { }

    public static Notificacion Crear(EnvioSeguido envio, Guid eventoId, string estadoEnvio, DateTimeOffset creadaEn)
    {
        if (eventoId == Guid.Empty || string.IsNullOrWhiteSpace(estadoEnvio))
            throw new DomainException("La notificación necesita el evento y el estado del envío.");
        // Un correo mal escrito tampoco se puede usar: reintentar no lo arregla (A2).
        var enviable = envio.EmailDestinatario is { } email && MailAddress.TryCreate(email, out _);

        return new()
        {
            OperadorId = envio.OperadorId,
            ComercioId = envio.ComercioId,
            EnvioId = envio.Id,
            EventoId = eventoId,
            Canal = CanalNotificacion.Email,
            Tipo = TipoNotificacion.CambioDeEstado,
            EstadoEnvio = estadoEnvio,
            Destino = envio.EmailDestinatario,
            Estado = enviable ? EstadoNotificacion.Pendiente : EstadoNotificacion.NoEnviable,
            CreadaEn = creadaEn,
        };
    }

    public void RegistrarEnvio(DateTimeOffset enviadaEn)
    {
        ExigirPorEnviar();
        Intentos++;
        Estado = EstadoNotificacion.Enviada;
        EnviadaEn = enviadaEn;
        UltimoError = null;
    }

    // ultimo: la mensajería ya agotó los reintentos (ADR-0003, sección 2.6); el mensaje va a la cola de fallidos.
    public void RegistrarFallo(string? error, bool ultimo)
    {
        ExigirPorEnviar();
        Intentos++;
        Estado = ultimo ? EstadoNotificacion.Fallida : EstadoNotificacion.Pendiente;
        UltimoError = error is { Length: > LargoError } ? error[..LargoError] : error;
    }

    private void ExigirPorEnviar()
    {
        if (!PorEnviar) throw new DomainException("La notificación ya se envió o no tiene a quién enviarse.");
    }
}
