namespace Logistica.Modules.Envios.Contracts.Events;

// Datos del hecho (CU-19): el despachador eligió la fecha y la franja del nuevo intento de un envío reprogramado.
// El Worker avisa al destinatario. Identificador, inquilino, fecha y traza pertenecen al sobre del Outbox.
public sealed record EnvioReprogramado(Guid EnvioId, string Numero, DateOnly Fecha, Guid FranjaId, string Origen)
{
    // Nombre versionado del evento; también es la routing key en RabbitMQ.
    public const string Tipo = "EnvioReprogramado.v1";
}
