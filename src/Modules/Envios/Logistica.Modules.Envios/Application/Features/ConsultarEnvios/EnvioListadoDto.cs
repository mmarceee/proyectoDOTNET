namespace Logistica.Modules.Envios.Application.Features.ConsultarEnvios;

internal sealed record EnvioListadoDto(
    string Numero,
    Guid ComercioId,
    string DestinatarioNombre,
    string Localidad,
    string Estado,
    DateTimeOffset CreadoEn,
    decimal MontoTarifa);
