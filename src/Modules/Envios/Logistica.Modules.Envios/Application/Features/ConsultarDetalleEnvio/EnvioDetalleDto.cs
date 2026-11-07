namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed record EnvioDetalleDto(
    string Numero,
    Guid ComercioId,
    string Estado,
    DateTimeOffset CreadoEn,
    decimal MontoTarifa,
    DestinatarioDetalleDto Destinatario,
    DireccionDetalleDto Direccion,
    IReadOnlyList<BultoDetalleDto> Bultos,
    IReadOnlyList<EventoEnvioDetalleDto> Eventos,
    VersionTarifarioDetalleDto? VersionTarifario,
    IReadOnlyList<IntentoEntregaDetalleDto> Intentos,
    IReadOnlyList<IncidenciaDetalleDto> Incidencias,
    DevolucionDetalleDto? Devolucion,
    string Modalidad = "Estandar",
    // RF 31: el resumen derivado de los bultos, si ya terminó y cuántos bultos se entregaron (el total es Bultos.Count).
    string Resumen = "Creado",
    bool Terminado = false,
    int BultosEntregados = 0,
    // RF 33: los recargos de las visitas adicionales, con el monto registrado al realizarlas.
    IReadOnlyList<RecargoDetalleDto>? Recargos = null);

internal sealed record RecargoDetalleDto(DateTimeOffset OcurridoEn, decimal Monto);
