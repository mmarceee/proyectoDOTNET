namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record IntentoEntregaDetalleDto(
    int NumeroIntento, DateTimeOffset FechaHora, string Resultado,
    Guid? MotivoNoEntregaId, string? Observaciones, PruebaEntregaDetalleDto? Evidencia);

internal sealed record PruebaEntregaDetalleDto(
    Guid? FirmaArchivoId, Guid? FotoArchivoId, string? NombreReceptor, string? DocumentoReceptor,
    decimal Latitud, decimal Longitud, DateTimeOffset CapturadaEn);
