namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record IncidenciaDetalleDto(
    string Tipo, string Descripcion, string Estado, DateTimeOffset CreadaEn,
    DateTimeOffset? ResueltaEn, string? Resolucion);
