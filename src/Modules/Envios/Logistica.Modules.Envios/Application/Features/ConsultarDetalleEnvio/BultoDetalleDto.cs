namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record BultoDetalleDto(
    string Codigo,
    decimal PesoKg,
    decimal LargoCm,
    decimal AnchoCm,
    decimal AltoCm,
    decimal MontoTarifa);
