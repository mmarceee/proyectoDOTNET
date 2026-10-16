namespace Logistica.Modules.Envios.Application.Features.ReprogramarEnvio;

// Lo que elige el despachador (CU-19, paso 3): la fecha y la franja del nuevo intento.
internal sealed record ReprogramarEnvioCommand(Guid EnvioId, DateOnly Fecha, Guid FranjaId);
