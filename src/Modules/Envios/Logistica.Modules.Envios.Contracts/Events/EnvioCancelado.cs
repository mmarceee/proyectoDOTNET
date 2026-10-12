namespace Logistica.Modules.Envios.Contracts.Events;

// Datos del hecho (CU-16, T17): el comercio canceló un envío antes de que llegara al depósito.
// Identificador, inquilino, fecha y traza pertenecen al sobre del Outbox.
public sealed record EnvioCancelado(Guid EnvioId, string Numero, string? Motivo, string Origen)
{
    // Nombre versionado del evento; también es la routing key en RabbitMQ.
    public const string Tipo = "EnvioCancelado.v1";
}
