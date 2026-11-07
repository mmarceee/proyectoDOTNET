namespace Logistica.Modules.Envios.Contracts.Results;

// Lo que el seguimiento público puede mostrar de un envío (CU-60, paso 3). Sólo lo necesario y nada de terceros: el
// nombre de pila del destinatario y la localidad, sin dirección completa, teléfono ni documento; el historial sin el
// personal del operador, el origen ni el detalle.
// RF 35: el resumen derivado de los bultos (RF 31), el contador de entregados y cada bulto con su estado, sin su Id.
public sealed record SeguimientoEnvio(
    Guid EnvioId, string Numero, string Estado, string NombreDePila, string Localidad,
    DateTimeOffset CreadoEn, DateOnly? FechaEntregaProgramada, Guid? FranjaHorariaId, Guid? VersionReglasId,
    IReadOnlyList<EventoSeguimiento> Historial,
    string Resumen, bool Terminado, int BultosEntregados, IReadOnlyList<BultoSeguimiento> Bultos);

// Un cambio de estado del historial público.
public sealed record EventoSeguimiento(string Estado, DateTimeOffset OcurridoEn);

// RF 35: un bulto como lo ve la destinataria, "sin exponer identificadores internos": ni el Id ni el código de la
// etiqueta, sólo su número dentro del envío (1, 2, 3). Decisión del equipo del 07/11/2026.
public sealed record BultoSeguimiento(int Numero, string Estado);
