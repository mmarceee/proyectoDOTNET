using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;

namespace Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;

// La dirección pública del sitio (Aplicacion:UrlPublica), para los enlaces absolutos de los correos. Se valida al usarla,
// así la API y el Worker arrancan aunque falte, y el error dice qué configurar.
internal sealed class DireccionPublica(string? valor)
{
    private readonly Lazy<string> _base = new(() =>
        Uri.TryCreate(valor?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : throw new InvalidOperationException(
                "Configurá Aplicacion:UrlPublica con la dirección pública del sitio (por ejemplo, http://localhost:8080)."));

    public string Absoluta(string rutaRelativa) => _base.Value + rutaRelativa;
}

// El servidor de correo no aceptó el mensaje. El intento ya quedó registrado; la excepción hace que la mensajería
// reintente con espera creciente o, agotados los reintentos, pase el mensaje a la cola de fallidos (ADR-0003, sección 2.6).
internal sealed class NotificacionNoEnviadaException(Guid notificacionId)
    : Exception($"La notificación {notificacionId} no se pudo enviar.");

// CU-62: notifica al destinatario un hecho relevante de su envío, con la marca del operador y el enlace de seguimiento.
internal sealed class NotificarDestinatarioHandler(EnviosSeguidos envios, INotificacionesRepository notificaciones,
    IIdentidadesOperadorRepository identidades, IEmisorCorreo emisor, TokenSeguimiento tokens, DireccionPublica direccion,
    TimeProvider reloj)
{
    // Para un operador que todavía no guardó su identidad (CU-07): el seguimiento público muestra la suya.
    internal static readonly MarcaCorreo MarcaNeutra = new("Seguimiento de envíos", "#1F4E79", null, null);

    public async Task HandleAsync(HechoEnvio hecho, bool ultimoIntento, CancellationToken ct)
    {
        var envio = await envios.ObtenerOCrearAsync(hecho, ct);
        var notificacion = await notificaciones.ObtenerPorEventoAsync(hecho.EventoId, ct);
        if (notificacion is null)
        {
            notificacion = Notificacion.Crear(envio, hecho.EventoId, hecho.EstadoEnvio, reloj.GetUtcNow());
            notificaciones.Agregar(notificacion);
        }

        // Ya enviada (el mismo evento llegó de nuevo) o sin correo del destinatario (A2): no se envía nada.
        if (!notificacion.PorEnviar)
        {
            await notificaciones.GuardarAsync(ct);
            return;
        }

        var identidad = await identidades.ObtenerAsync(envio.OperadorId, ct);
        var marca = identidad is null ? MarcaNeutra
            : new MarcaCorreo(identidad.Nombre, identidad.ColorPrimario, identidad.EmailContacto, identidad.TelefonoContacto);
        var texto = TextosNotificacion.Para(hecho, envio.Numero, marca.Nombre);
        var enlace = direccion.Absoluta($"/seguimiento/{tokens.Generar(envio.OperadorId, envio.Id)}");
        var resultado = await emisor.EnviarAsync(new MensajeCorreo(notificacion.Id, notificacion.Destino!,
            envio.NombreDestinatario, texto.Asunto, texto.Titulo, texto.Texto, enlace, marca), ct);

        if (resultado.Exitoso) notificacion.RegistrarEnvio(reloj.GetUtcNow());
        else notificacion.RegistrarFallo(resultado.Error, ultimoIntento);
        await notificaciones.GuardarAsync(ct);
        if (!resultado.Exitoso) throw new NotificacionNoEnviadaException(notificacion.Id);
    }
}
