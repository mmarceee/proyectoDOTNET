namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record EventoEnvioDetalleDto(
    string? EstadoAnterior,
    string EstadoNuevo,
    DateTimeOffset OcurridoEn,
    string Origen,
    Guid? ResponsableId,
    decimal? Latitud,
    decimal? Longitud,
    string? Detalle);
