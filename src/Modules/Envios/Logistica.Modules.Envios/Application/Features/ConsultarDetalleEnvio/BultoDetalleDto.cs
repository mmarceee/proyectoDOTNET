namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record BultoDetalleDto(
    Guid Id,
    string Codigo,
    decimal PesoKg,
    decimal LargoCm,
    decimal AnchoCm,
    decimal AltoCm,
    decimal MontoTarifa,
    bool Recibido = false,
    bool PuedeRecepcionar = false,
    // RF 31: el estado operativo del bulto y sus intentos reales fallidos.
    string Estado = "Admitido",
    int IntentosFallidos = 0);
