using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento.Application.Features.EntregarAvisos;
using Logistica.Modules.Seguimiento.Contracts.Events;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// CU-64: envía una entrega de aviso. Cada AvisoPendiente se reintenta solo, así un receptor caído no hace reenviar
// los avisos que otras suscripciones ya recibieron.
internal sealed class EntregarAviso(SeguimientoDbContext db, EntregarAvisoHandler handler) : IConsumidorIntegracion
{
    public const string Cola = "seguimiento.entregar-aviso";

    public async Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
    {
        if (mensaje.Tipo != AvisoPendiente.Tipo)
            throw new InvalidDataException("Evento inválido para la entrega de avisos.");
        var evento = JsonSerializer.Deserialize<AvisoPendiente>(mensaje.Contenido);
        if (evento is null || evento.EntregaId == Guid.Empty)
            throw new InvalidDataException("El aviso pendiente no tiene la entrega.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Inbox.AnyAsync(m => m.MessageId == mensaje.Id && m.Consumidor == Cola, ct))
        {
            // Sin esperas por delante, un fallo deja la entrega en la cola de fallidos (ADR-0003, sección 2.6).
            var ultimo = mensaje.Intento >= RabbitMqTransport.Esperas.Length;
            try
            {
                await handler.HandleAsync(evento.EntregaId, ultimo, ct);
            }
            catch (AvisoNoEntregadoException)
            {
                // El intento fallido queda registrado; la excepción sigue hacia los reintentos.
                await tx.CommitAsync(ct);
                throw;
            }
            db.Inbox.Add(new() { MessageId = mensaje.Id, Consumidor = Cola, ProcesadoEn = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
