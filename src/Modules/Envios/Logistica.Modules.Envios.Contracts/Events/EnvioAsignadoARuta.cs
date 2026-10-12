namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el envío quedó asignado a una ruta (CU-40).
// Viaja por Outbox. El sobre (MessageId, inquilino, fecha y traza) lo agrega la tabla outbox, no este registro.
public sealed record EnvioAsignadoARuta(Guid EnvioId, Guid RutaId, Guid? ResponsableId, string Origen)
{
    // Nombre versionado del evento; también es la routing key en RabbitMQ.
    public const string Tipo = "EnvioAsignadoARuta.v1";
}
