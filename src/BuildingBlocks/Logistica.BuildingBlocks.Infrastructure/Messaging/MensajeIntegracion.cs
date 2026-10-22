namespace Logistica.BuildingBlocks.Infrastructure.Messaging;

// Sobre de transporte. El inquilino viene de las cabeceras AMQP, nunca del JSON del evento.
public sealed record MensajeIntegracion(Guid Id, string Tipo, Guid OperadorId, Guid? ComercioId,
    DateTimeOffset OcurridoEn, string? TraceParent, string Contenido)
{
    // Veces que el mensaje ya falló (cabecera "intento"); 0 en la primera entrega. Cuando llega a
    // RabbitMqTransport.Esperas.Length, un nuevo fallo lo manda a la cola de fallidos (ADR-0003, sección 2.6).
    public int Intento { get; init; }
}

public interface IConsumidorIntegracion
{
    Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct);
}

public sealed record SuscripcionIntegracion(string Cola, string Tipo);
