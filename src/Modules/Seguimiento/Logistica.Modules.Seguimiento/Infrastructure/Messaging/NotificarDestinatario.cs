using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// CU-62: notifica al destinatario los cambios relevantes de su envío. Una cola por tipo de evento; la cola es la
// selección de lo relevante (paso 3): los demás eventos no llegan (A1).
internal sealed class NotificarDestinatario(SeguimientoDbContext db, NotificarDestinatarioHandler handler) : IConsumidorIntegracion
{
    // Admitido (con el enlace), en tránsito, no entregado (reprogramado o en devolución), reprogramado, entregado y la
    // devolución manual (CU-20).
    public static readonly string[] Tipos =
    [
        EnvioAdmitido.Tipo, EnvioEnTransito.Tipo, IntentoFallidoRegistrado.Tipo, EnvioReprogramado.Tipo, EnvioEntregado.Tipo,
        DevolucionIniciada.Tipo,
    ];

    public static string Cola(string tipo) => $"seguimiento.notificar-destinatario.{tipo}";

    public async Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
    {
        var hecho = LectorHechosEnvio.Leer(mensaje);
        var consumidor = Cola(mensaje.Tipo);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Inbox.AnyAsync(m => m.MessageId == mensaje.Id && m.Consumidor == consumidor, ct))
        {
            // Sin esperas por delante, un fallo deja la notificación Fallida (ADR-0003, sección 2.6).
            var ultimo = mensaje.Intento >= RabbitMqTransport.Esperas.Length;
            try
            {
                await handler.HandleAsync(hecho, ultimo, ct);
            }
            catch (NotificacionNoEnviadaException)
            {
                // El intento fallido queda registrado; la excepción sigue hacia los reintentos.
                await tx.CommitAsync(ct);
                throw;
            }
            db.Inbox.Add(new() { MessageId = mensaje.Id, Consumidor = consumidor, ProcesadoEn = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
