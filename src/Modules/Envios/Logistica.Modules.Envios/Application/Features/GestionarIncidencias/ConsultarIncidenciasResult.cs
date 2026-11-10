namespace Logistica.Modules.Envios.Application.Features.GestionarIncidencias;

// Una fila del listado, con el número del envío para volver a su detalle.
internal sealed record IncidenciaListadoDto(
    Guid Id,
    string NumeroEnvio,
    string Tipo,
    string Descripcion,
    string Estado,
    DateTimeOffset CreadaEn,
    DateTimeOffset? ResueltaEn,
    string? Resolucion);

internal sealed record ConsultarIncidenciasResult(
    IReadOnlyList<IncidenciaListadoDto> Incidencias,
    int Pagina,
    int TamanoPagina,
    int TotalRegistros)
{
    public int TotalPaginas => TotalRegistros == 0 ? 0 : (int)Math.Ceiling((double)TotalRegistros / TamanoPagina);
}
