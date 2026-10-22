using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Administracion.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// CU-62: copia la identidad visual que guardó el administrador (CU-07) para los correos al destinatario.
internal sealed class ActualizarIdentidadOperador(ActualizarIdentidadOperadorHandler handler) : IConsumidorIntegracion
{
    public const string Cola = "seguimiento.actualizar-identidad-operador";

    public Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
    {
        if (mensaje.Tipo != IdentidadVisualActualizada.Tipo)
            throw new InvalidDataException("Evento inválido para la identidad del operador.");
        var evento = JsonSerializer.Deserialize<IdentidadVisualActualizada>(mensaje.Contenido);
        if (evento is null || string.IsNullOrWhiteSpace(evento.NombreOperador)
            || string.IsNullOrWhiteSpace(evento.ColorPrimario) || string.IsNullOrWhiteSpace(evento.ColorSecundario))
            throw new InvalidDataException("La identidad del operador está incompleta.");
        return handler.HandleAsync(new IdentidadRecibida(evento.NombreOperador, evento.ColorPrimario, evento.ColorSecundario,
            evento.EmailContacto, evento.TelefonoContacto, mensaje.OcurridoEn), ct);
    }
}
