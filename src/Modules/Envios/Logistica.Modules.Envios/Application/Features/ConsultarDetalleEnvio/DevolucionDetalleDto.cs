namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record DevolucionDetalleDto(
    string Motivo, string Estado, DateTimeOffset IniciadaEn, DateTimeOffset? RecibidaEnDepositoEn,
    string? NombreReceptor, string? DocumentoReceptor, DateTimeOffset? EntregadaAlComercioEn);
