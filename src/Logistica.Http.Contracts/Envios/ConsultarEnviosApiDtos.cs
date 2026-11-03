namespace Logistica.Http.Contracts.Envios;

public sealed record PaginaEnviosApiResponse(int Pagina, int TamanoPagina, int Total,
    IReadOnlyList<ResumenEnvioApiResponse> Envios);

public sealed record ResumenEnvioApiResponse(string Numero, string? ReferenciaComercio, DateTimeOffset CreadoEn,
    decimal Tarifa, string Modalidad, string Estado, bool Terminado, int TotalBultos, int Entregados,
    ResultadosBultosApiResponse Resultados);

public sealed record ResultadosBultosApiResponse(int Pendientes, int Devueltos, int Extraviados, int Cancelados);

public sealed record DetalleEnvioApiResponse(ResumenEnvioApiResponse Envio,
    IReadOnlyList<BultoEnvioApiResponse> Bultos, IReadOnlyList<EventoEnvioApiResponse> Historial);

public sealed record BultoEnvioApiResponse(string Codigo, string? Estado);

public sealed record EventoEnvioApiResponse(string Tipo, DateTimeOffset OcurridoEn);
