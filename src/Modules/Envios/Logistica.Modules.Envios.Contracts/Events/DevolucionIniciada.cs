namespace Logistica.Modules.Envios.Contracts.Events;

// Datos del hecho (CU-20, T12 o T14): el despachador o el comercio decidieron devolver un envío al comercio.
// El Worker avisa al comercio. Identificador, inquilino, fecha y traza pertenecen al sobre del Outbox.
public sealed record DevolucionIniciada(Guid EnvioId, string Numero, Guid DevolucionId, string EstadoAnterior,
    string Motivo, Guid? ResponsableId, string Origen)
{
    // Nombre versionado del evento; también es la routing key en RabbitMQ.
    public const string Tipo = "DevolucionIniciada.v1";
}
