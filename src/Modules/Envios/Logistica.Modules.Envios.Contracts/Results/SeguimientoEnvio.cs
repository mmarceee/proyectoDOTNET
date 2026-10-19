namespace Logistica.Modules.Envios.Contracts.Results;

// Lo que el seguimiento público puede mostrar de un envío (CU-60, paso 3). Sólo lo necesario y nada de terceros: el
// nombre de pila del destinatario y la localidad, sin dirección completa, teléfono ni documento; el historial sin el
// personal del operador, el origen ni el detalle.
public sealed record SeguimientoEnvio(
    Guid EnvioId, string Numero, string Estado, string NombreDePila, string Localidad,
    DateTimeOffset CreadoEn, DateOnly? FechaEntregaProgramada, Guid? FranjaHorariaId, Guid? VersionReglasId,
    IReadOnlyList<EventoSeguimiento> Historial);

// Un cambio de estado del historial público.
public sealed record EventoSeguimiento(string Estado, DateTimeOffset OcurridoEn);
