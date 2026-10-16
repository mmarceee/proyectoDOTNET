namespace Logistica.BuildingBlocks.Infrastructure.Messaging;

// Sobre de transporte. El inquilino viene de las cabeceras AMQP, nunca del JSON del evento.
public sealed record MensajeIntegracion(Guid Id, string Tipo, Guid OperadorId, Guid? ComercioId,
    DateTimeOffset OcurridoEn, string? TraceParent, string Contenido);

public interface IConsumidorIntegracion
{
    Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct);
}

public sealed record SuscripcionIntegracion(string Cola, string Tipo);
