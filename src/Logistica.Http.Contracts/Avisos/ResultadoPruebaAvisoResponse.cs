namespace Logistica.Http.Contracts.Avisos;

public sealed record ResultadoPruebaAvisoResponse(Guid MensajeId, bool Exitosa, int? CodigoRespuesta, string? Error);
