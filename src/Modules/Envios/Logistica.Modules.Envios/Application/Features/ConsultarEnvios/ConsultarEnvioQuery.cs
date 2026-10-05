namespace Logistica.Modules.Envios.Application.Features.ConsultarEnvios;

internal sealed record ConsultarEnviosQuery(
    int Pagina = 1,
    int TamanoPagina = 20);
