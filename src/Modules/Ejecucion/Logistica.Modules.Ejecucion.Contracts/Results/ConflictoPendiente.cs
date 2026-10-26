namespace Logistica.Modules.Ejecucion.Contracts.Results;

// CU-66 (ADR-0004, sección 2.5): una operación de la PWA que el servidor rechazó y que el despachador todavía no
// revisó. La evidencia completa sigue en Ejecución; acá va lo necesario para mostrarla en el tablero.
public sealed record ConflictoPendiente(Guid OperacionId, Guid RutaId, Guid? EnvioId, Guid RepartidorId, string Tipo,
    string Resultado, string? Motivo, DateTimeOffset OcurridoEn, DateTimeOffset RecibidoEn);
