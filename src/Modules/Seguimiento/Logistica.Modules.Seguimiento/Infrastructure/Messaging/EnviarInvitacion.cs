using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Administracion.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.EnviarInvitacion;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// CU-02, paso 7: manda el correo de invitación de un usuario nuevo o reinvitado (A6). El inbox evita mandar dos veces
// el mismo mensaje; si el correo falla, no se registra y la mensajería lo reintenta (ADR-0003, secciones 2.4 y 2.6).
internal sealed class EnviarInvitacion(EnviarInvitacionHandler handler, SeguimientoDbContext db) : IConsumidorIntegracion
{
    public const string Cola = "seguimiento.enviar-invitacion";

    public async Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
    {
        if (mensaje.Tipo != UsuarioInvitado.Tipo || mensaje.OperadorId == Guid.Empty)
            throw new InvalidDataException("Evento o inquilino inválido para la invitación.");
        var evento = JsonSerializer.Deserialize<UsuarioInvitado>(mensaje.Contenido);
        if (evento is null || string.IsNullOrWhiteSpace(evento.Email) || !evento.Enlace.StartsWith('/'))
            throw new InvalidDataException("La invitación está incompleta.");

        if (await db.Inbox.AnyAsync(m => m.MessageId == mensaje.Id && m.Consumidor == Cola, ct)) return;

        await handler.HandleAsync(new InvitacionRecibida(mensaje.Id, evento.Email, evento.Nombre, evento.Perfil,
            evento.Enlace, evento.Reenvio), ct);

        db.Inbox.Add(new() { MessageId = mensaje.Id, Consumidor = Cola, ProcesadoEn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }
}
