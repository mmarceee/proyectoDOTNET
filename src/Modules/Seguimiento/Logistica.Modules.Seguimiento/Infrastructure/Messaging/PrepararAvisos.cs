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
        var hecho = LectorHechosEnvio.Leer(mensaje);
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
}
