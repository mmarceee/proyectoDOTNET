namespace Logistica.Http.Contracts.Envios;

public sealed record ZonaEnviosPortal(Guid Id, string Nombre);

public sealed record EnvioPortalFila(string Numero, string? Referencia, string Destinatario, string Localidad,
    string? Zona, string Modalidad, string Resumen, bool Terminado, int Entregados, int TotalBultos,
    DateTimeOffset CreadoEn, decimal Monto, bool PuedeImprimirEtiquetas = false);

public sealed record PaginaEnviosPortal(IReadOnlyList<EnvioPortalFila> Envios, int Pagina, int TotalPaginas,
    int TotalRegistros, IReadOnlyList<ZonaEnviosPortal> Zonas);

public sealed record BultoEnvioPortal(string Codigo, string Estado);
public sealed record DetalleEnvioPortal(string Numero, string Resumen, bool Terminado,
    int Entregados, IReadOnlyList<BultoEnvioPortal> Bultos, bool PuedeImprimirEtiquetas = false);
