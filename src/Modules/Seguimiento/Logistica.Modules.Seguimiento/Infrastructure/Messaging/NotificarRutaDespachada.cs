using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

internal sealed class NotificarRutaDespachada(SeguimientoDbContext db, ICurrentTenant tenant) : IConsumidorIntegracion
{
    public const string Cola = "seguimiento.notificar-ruta-despachada";

    public async Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
    {
        if (mensaje.Tipo != RutaDespachada.Tipo || mensaje.OperadorId == Guid.Empty || tenant.OperadorId != mensaje.OperadorId)
            throw new InvalidDataException("Evento o inquilino inválido para el aviso de despacho.");
        var evento = JsonSerializer.Deserialize<RutaDespachada>(mensaje.Contenido)
            ?? throw new InvalidDataException("El evento de despacho está vacío.");
        if (evento.RutaId == Guid.Empty || evento.RepartidorId == Guid.Empty || evento.Revision < 1 || evento.Fecha == default)
            throw new InvalidDataException("El evento de despacho está incompleto.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // La clave compuesta arbitra también consumidores simultáneos. Si pierde el INSERT,
        // toda la transacción se revierte y el reintento verá el mensaje ya procesado.
        if (!await db.Inbox.AnyAsync(m => m.MessageId == mensaje.Id && m.Consumidor == Cola, ct))
        {
            if (!await db.Avisos.AnyAsync(a => a.RutaId == evento.RutaId && a.Revision == evento.Revision, ct))
                db.Avisos.Add(AvisoRuta.Crear(mensaje.OperadorId, evento.RepartidorId, evento.RutaId,
                    evento.Fecha, evento.Revision, mensaje.OcurridoEn));
            db.Inbox.Add(new() { MessageId = mensaje.Id, Consumidor = Cola, ProcesadoEn = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
