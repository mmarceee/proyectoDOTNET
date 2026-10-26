namespace Logistica.Modules.Envios.Contracts.Results;

// CU-66: lo que el tablero necesita de un envío. Con el estado se calcula el avance de su ruta; con la fecha y la franja,
// si está en riesgo (sólo si todavía espera la entrega).
public sealed record EnvioParaTablero(Guid EnvioId, string Numero, string Estado, DateOnly? FechaEntregaProgramada,
    Guid? FranjaHorariaId, bool EntregaPendiente);
