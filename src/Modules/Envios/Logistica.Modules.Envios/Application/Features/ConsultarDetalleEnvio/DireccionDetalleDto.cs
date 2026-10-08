namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record DireccionDetalleDto(
    string Calle,
    string Numero,
    string Localidad,
    string Departamento,
    string CodigoPostal,
    string? Referencia);
