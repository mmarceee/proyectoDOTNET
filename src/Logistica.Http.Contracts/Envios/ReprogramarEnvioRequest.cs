namespace Logistica.Http.Contracts.Envios;

// Cuerpo de POST /api/envios/{id}/reprogramacion (CU-19): la fecha y la franja del nuevo intento.
public sealed record ReprogramarEnvioRequest(DateOnly Fecha, Guid FranjaId);
