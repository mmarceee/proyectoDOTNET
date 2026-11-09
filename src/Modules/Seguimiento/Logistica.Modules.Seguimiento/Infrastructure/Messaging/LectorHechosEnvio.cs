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

        // Los campos opcionales que no vienen (eventos publicados antes de agregarlos) quedan en null.
        Guid? Identificador(string nombre) =>
            raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String
                && valor.TryGetGuid(out var guid) && guid != Guid.Empty ? guid : null;

        DateTimeOffset? Fecha(string nombre) =>
            raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String
                && valor.TryGetDateTimeOffset(out var fecha) ? fecha : null;

        string EstadoDelEnvio() => Texto("EstadoEnvio") is { Length: > 0 } estado
            ? estado : throw new InvalidDataException("El evento del bulto no tiene el estado del envío.");

        string CodigoBulto() => Texto("CodigoBulto") is { Length: > 0 } codigo
            ? codigo : throw new InvalidDataException("El evento del bulto no tiene su código.");

        HechoEnvio Hecho(string estado, bool intentoFallido = false, bool esAlta = false) =>
            new(mensaje.Id, envioId, Texto("Numero"), estado, intentoFallido, mensaje.OcurridoEn, esAlta);

        return mensaje.Tipo switch
        {
            EnvioAdmitido.Tipo => Hecho("Admitido", esAlta: true) with
            {
                NombreDestinatario = Texto("NombreDestinatario"),
                EmailDestinatario = Texto("EmailDestinatario"),
                EsPrueba = raiz.TryGetProperty("EsPrueba", out var prueba) && prueba.ValueKind == JsonValueKind.True,
                ZonaId = Identificador("ZonaId"),
                EntregaComprometidaHasta = Fecha("EntregaComprometidaHasta"),
            },
            EnvioAsignadoARuta.Tipo => Hecho("AsignadoARuta"),
            EnvioDesasignadoDeRuta.Tipo => Hecho("EnDeposito"),
            EnvioEnTransito.Tipo => Hecho("EnTransito"),
            EnvioEntregado.Tipo => Hecho("Entregado") with { RepartidorId = Identificador("RepartidorId") },
            // T7 y, en la misma operación, T9 o T10: el hecho lleva el estado en que quedó el envío.
            IntentoFallidoRegistrado.Tipo => Hecho(
                Texto("EstadoFinal") is { Length: > 0 } estado
                    ? estado : throw new InvalidDataException("El intento fallido no tiene el estado final."),
                intentoFallido: true) with { Motivo = Texto("Motivo"), RepartidorId = Identificador("RepartidorId") },
            EnvioReprogramado.Tipo => Hecho("Reprogramado") with
            {
                // System.Text.Json escribe DateOnly como yyyy-MM-dd.
                NuevaFecha = DateOnly.TryParseExact(Texto("Fecha"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fecha) ? fecha : null,
            },
            EnvioCancelado.Tipo => Hecho("Cancelado"),
            EnvioReintegradoADeposito.Tipo => Hecho("EnDeposito"),
            // T2 (CU-30): el depósito recibió el envío.
            EnvioRecibidoEnDeposito.Tipo => Hecho("EnDeposito"),
            // T15 (CU-31): el comercio recibió el envío devuelto. Con bultos ya entregados, el cierre es mixto (RF 36).
            EnvioDevuelto.Tipo => Hecho("Devuelto") with
            {
                CierreMixto = raiz.TryGetProperty("BultosEntregados", out var entregados)
                    && entregados.ValueKind == JsonValueKind.Number && entregados.GetInt32() > 0,
            },
            // RF 36: los hechos de cada bulto, sólo para los avisos v2. El estado del hecho es el del envío.
            BultoEntregado.Tipo => Hecho(EstadoDelEnvio()) with
            {
                Bulto = new(CodigoBulto(), "Entregado"),
                RepartidorId = Identificador("RepartidorId"),
            },
            BultoNoEntregado.Tipo => Hecho(EstadoDelEnvio(), intentoFallido: true) with
            {
                Bulto = new(CodigoBulto(), Texto("EstadoBulto") is { Length: > 0 } estadoBulto
                    ? estadoBulto : throw new InvalidDataException("El bulto no entregado no tiene su estado."), Texto("Motivo")),
                Motivo = Texto("Motivo"),
                RepartidorId = Identificador("RepartidorId"),
            },
            BultoDevuelto.Tipo => Hecho(EstadoDelEnvio()) with { Bulto = new(CodigoBulto(), "Devuelto") },
            // CU-20: la devolución manual (T12 o T14); el clasificador la avisa como Devolucion. La automática de CU-18
            // ya llega como IntentoFallidoRegistrado con EstadoFinal EnDevolucion, así que no se avisa dos veces.
            DevolucionIniciada.Tipo => Hecho("EnDevolucion"),
            // CU-21 (T16): un cambio de estado más para el comercio.
            EnvioExtraviado.Tipo => Hecho("Extraviado"),
            _ => throw new InvalidDataException("Tipo de evento de Envíos desconocido."),
        };
    }
}
