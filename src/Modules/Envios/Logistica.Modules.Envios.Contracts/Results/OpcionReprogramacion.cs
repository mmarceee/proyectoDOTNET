namespace Logistica.Modules.Envios.Contracts.Results;

// CU-61, paso 2: una fecha y franja que el destinatario puede elegir para su nueva entrega. Desde y Hasta son la hora
// local del operador.
public sealed record OpcionReprogramacion(DateOnly Fecha, Guid FranjaId, TimeOnly Desde, TimeOnly Hasta);
