namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record DestinatarioDetalleDto(
    string Nombre,
    string Telefono,
    string? Email,
    string? Documento);
