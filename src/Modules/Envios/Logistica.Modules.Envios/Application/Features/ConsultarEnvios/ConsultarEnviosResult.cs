namespace Logistica.Modules.Envios.Application.Features.ConsultarEnvios;

internal sealed record ConsultarEnviosResult(
    IReadOnlyList<EnvioListadoDto> Envios,
    int Pagina,
    int TamanoPagina,
    int TotalRegistros)
{
    public int TotalPaginas =>
        TotalRegistros == 0
            ? 0
            : (int)Math.Ceiling((double)TotalRegistros / TamanoPagina);
}
