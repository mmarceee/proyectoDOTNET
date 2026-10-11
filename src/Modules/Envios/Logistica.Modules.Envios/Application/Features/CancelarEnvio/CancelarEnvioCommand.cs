namespace Logistica.Modules.Envios.Application.Features.CancelarEnvio;

// Lo que hace falta para cancelar un envío, ya traducido desde HTTP (CU-16). El motivo es opcional (paso 2).
internal sealed record CancelarEnvioCommand(Guid EnvioId, string? Motivo);
