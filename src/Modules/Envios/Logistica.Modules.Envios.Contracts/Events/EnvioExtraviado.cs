namespace Logistica.Modules.Envios.Contracts.Events;

// Datos del hecho (CU-21, T16): el administrador o el despachador declaró extraviado un envío. Es terminal.
// El Worker avisa al comercio y al destinatario. Identificador, inquilino, fecha y traza pertenecen al sobre del Outbox.
public sealed record EnvioExtraviado(Guid EnvioId, string Numero, string EstadoAnterior, string Motivo,
    Guid? ResponsableId, string Origen)
{
    // Nombre versionado del evento; también es la routing key en RabbitMQ.
    public const string Tipo = "EnvioExtraviado.v1";
}
