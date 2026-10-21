using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.EntregarAvisos;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// CU-64: ante cada cambio de estado de un envío, prepara los avisos a los comercios suscritos. Una cola por tipo de
// evento, como InvalidarSeguimiento. Inbox, entregas y outbox se confirman en la misma transacción (ADR-0003, sección 2.5).
internal sealed class PrepararAvisos(SeguimientoDbContext db, PrepararAvisosHandler handler) : IConsumidorIntegracion
{
    public static readonly string[] Tipos =
    [
        EnvioAdmitido.Tipo, EnvioAsignadoARuta.Tipo, EnvioDesasignadoDeRuta.Tipo, EnvioEnTransito.Tipo, EnvioEntregado.Tipo,
        IntentoFallidoRegistrado.Tipo, EnvioReprogramado.Tipo, EnvioCancelado.Tipo, EnvioReintegradoADeposito.Tipo,
    ];

    public static string Cola(string tipo) => $"seguimiento.preparar-avisos.{tipo}";

    public async Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
    {
        var hecho = Leer(mensaje);
        var consumidor = Cola(mensaje.Tipo);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Inbox.AnyAsync(m => m.MessageId == mensaje.Id && m.Consumidor == consumidor, ct))
        {
            await handler.HandleAsync(hecho, ct);
            db.Inbox.Add(new() { MessageId = mensaje.Id, Consumidor = consumidor, ProcesadoEn = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    // El contenido aporta sólo datos del hecho; el inquilino viene del sobre. La fecha del evento es la del sobre.
    internal static HechoEnvio Leer(MensajeIntegracion mensaje)
    {
        using var json = JsonDocument.Parse(mensaje.Contenido);
        var raiz = json.RootElement;
        if (!raiz.TryGetProperty("EnvioId", out var id) || !id.TryGetGuid(out var envioId) || envioId == Guid.Empty)
            throw new InvalidDataException("El evento no tiene el Id del envío.");
        var numero = raiz.TryGetProperty("Numero", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

        HechoEnvio Hecho(string estado, bool intentoFallido = false, bool esAlta = false) =>
            new(mensaje.Id, envioId, numero, estado, intentoFallido, mensaje.OcurridoEn, esAlta);

        return mensaje.Tipo switch
        {
            EnvioAdmitido.Tipo => Hecho("Admitido", esAlta: true),
            EnvioAsignadoARuta.Tipo => Hecho("AsignadoARuta"),
            EnvioDesasignadoDeRuta.Tipo => Hecho("EnDeposito"),
            EnvioEnTransito.Tipo => Hecho("EnTransito"),
            EnvioEntregado.Tipo => Hecho("Entregado"),
            // T7 y, en la misma operación, T9 o T10: el aviso lleva el estado en que quedó el envío.
            IntentoFallidoRegistrado.Tipo => Hecho(
                raiz.TryGetProperty("EstadoFinal", out var final) && final.ValueKind == JsonValueKind.String
                    && final.GetString() is { Length: > 0 } estado
                    ? estado : throw new InvalidDataException("El intento fallido no tiene el estado final."),
                intentoFallido: true),
            EnvioReprogramado.Tipo => Hecho("Reprogramado"),
            EnvioCancelado.Tipo => Hecho("Cancelado"),
            EnvioReintegradoADeposito.Tipo => Hecho("EnDeposito"),
            _ => throw new InvalidDataException("Tipo de evento sin aviso al comercio."),
        };
    }
}
