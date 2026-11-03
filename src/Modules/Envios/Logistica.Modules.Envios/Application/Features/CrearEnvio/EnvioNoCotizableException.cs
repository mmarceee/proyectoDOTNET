using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.CrearEnvio;

internal sealed class EnvioNoCotizableException(string motivo) : DomainException(motivo);
