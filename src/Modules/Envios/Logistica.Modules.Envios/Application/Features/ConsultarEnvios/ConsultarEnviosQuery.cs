using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Application.Features.ConsultarEnvios;

internal sealed record ConsultarEnviosQuery(
    int Pagina = 1,
    int TamanoPagina = 20,
    EstadoEnvio? Estado = null,
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null,
    string? Texto = null,
    Guid? ComercioId = null);
