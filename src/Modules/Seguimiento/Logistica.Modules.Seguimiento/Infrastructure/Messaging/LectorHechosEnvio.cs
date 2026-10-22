using System.Globalization;
using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// Traduce un evento de Envíos al hecho que leen los avisos al comercio (CU-64) y las notificaciones al destinatario
// (CU-62). El contenido aporta sólo datos del hecho; el inquilino viene del sobre. La fecha del evento es la del sobre.
internal static class LectorHechosEnvio
{
    public static HechoEnvio Leer(MensajeIntegracion mensaje)
    {
        using var json = JsonDocument.Parse(mensaje.Contenido);
        var raiz = json.RootElement;
        if (!raiz.TryGetProperty("EnvioId", out var id) || !id.TryGetGuid(out var envioId) || envioId == Guid.Empty)
            throw new InvalidDataException("El evento no tiene el Id del envío.");

        string? Texto(string nombre) =>
            raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

        HechoEnvio Hecho(string estado, bool intentoFallido = false, bool esAlta = false) =>
            new(mensaje.Id, envioId, Texto("Numero"), estado, intentoFallido, mensaje.OcurridoEn, esAlta);

        return mensaje.Tipo switch
        {
            EnvioAdmitido.Tipo => Hecho("Admitido", esAlta: true) with
            {
                NombreDestinatario = Texto("NombreDestinatario"),
                EmailDestinatario = Texto("EmailDestinatario"),
            },
            EnvioAsignadoARuta.Tipo => Hecho("AsignadoARuta"),
            EnvioDesasignadoDeRuta.Tipo => Hecho("EnDeposito"),
            EnvioEnTransito.Tipo => Hecho("EnTransito"),
            EnvioEntregado.Tipo => Hecho("Entregado"),
            // T7 y, en la misma operación, T9 o T10: el hecho lleva el estado en que quedó el envío.
            IntentoFallidoRegistrado.Tipo => Hecho(
                Texto("EstadoFinal") is { Length: > 0 } estado
                    ? estado : throw new InvalidDataException("El intento fallido no tiene el estado final."),
                intentoFallido: true),
            EnvioReprogramado.Tipo => Hecho("Reprogramado") with
            {
                // System.Text.Json escribe DateOnly como yyyy-MM-dd.
                NuevaFecha = DateOnly.TryParseExact(Texto("Fecha"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fecha) ? fecha : null,
            },
            EnvioCancelado.Tipo => Hecho("Cancelado"),
            EnvioReintegradoADeposito.Tipo => Hecho("EnDeposito"),
            _ => throw new InvalidDataException("Tipo de evento de Envíos desconocido."),
        };
    }
}
