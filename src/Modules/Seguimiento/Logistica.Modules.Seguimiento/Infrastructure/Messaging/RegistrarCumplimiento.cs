using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.RecalcularIndicadores;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// CU-71: alimenta la tabla de lectura de los indicadores de cumplimiento. Una cola por tipo de evento, como
// PrepararAvisos; inbox y registro se confirman en la misma transacción, así un evento repetido no cuenta dos veces (A1).
internal sealed class RegistrarCumplimiento(SeguimientoDbContext db, RegistrarCumplimientoHandler handler) : IConsumidorIntegracion
{
    // El alta trae la zona y la ventana comprometida; la entrega y el intento fallido son lo que se cuenta.
    public static readonly string[] Tipos = [EnvioAdmitido.Tipo, EnvioEntregado.Tipo, IntentoFallidoRegistrado.Tipo];

    public static string Cola(string tipo) => $"seguimiento.indicadores.{tipo}";

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
