namespace Logistica.Http.Contracts.Avisos;

public sealed record EntregaAvisoResumen(Guid Id, DateTimeOffset FechaEvento, string NumeroEnvio,
    string EstadoEnvio, string Tipo, string Estado, int CantidadIntentos, bool SuscripcionActiva, bool PuedeReenviar);

public sealed record HistorialAvisosResponse(IReadOnlyList<EntregaAvisoResumen> Entregas,
    int Pagina, int TamanoPagina, int TotalRegistros);

public sealed record IntentoAvisoResponse(int Numero, DateTimeOffset Fecha, bool Exitoso, int? CodigoRespuesta, string? Error);

public sealed record DetalleEntregaAvisoResponse(EntregaAvisoResumen Entrega, string UrlDestino,
    IReadOnlyList<IntentoAvisoResponse> Intentos);

public sealed record ReenviarAvisosRequest(IReadOnlyList<Guid> EntregaIds);
public sealed record ReenvioAvisosResponse(int Cantidad, string Mensaje);
