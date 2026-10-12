namespace Logistica.Http.Contracts.Envios;

// Cuerpo de POST /api/envios/{id}/cancelacion (CU-16). El motivo es opcional; el Id del envío va en la URL.
public sealed record CancelarEnvioRequest(string? Motivo);
