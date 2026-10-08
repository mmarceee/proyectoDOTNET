namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record EnvioDetalleDto(
    string Numero,
    Guid ComercioId,
    string Estado,
    DateTimeOffset CreadoEn,
    decimal MontoTarifa,
    DestinatarioDetalleDto Destinatario,
    DireccionDetalleDto Direccion,
    IReadOnlyList<BultoDetalleDto> Bultos,
    IReadOnlyList<EventoEnvioDetalleDto> Eventos);
