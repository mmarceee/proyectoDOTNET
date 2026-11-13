namespace Logistica.Modules.Envios.Contracts.Results;

// CU-66: lo que el tablero necesita de un envío. Con la fecha y la franja se calcula si está en riesgo (sólo si todavía
// espera una entrega). Desde la entrega parcial (RF 31; cierre técnico, D4 y D5) el avance se decide por bulto y no por
// el estado de compatibilidad: Entregado es el resumen (todos los bultos entregados), EntregaPendiente es que algún bulto
// todavía tiene una entrega por delante, y BultosEntregados/BultosTotal muestran el progreso aunque el resumen no
// cambie (de 1/3 a 2/3 sigue siendo Entregado parcialmente). Estado es el de compatibilidad, sólo para mostrar.
public sealed record EnvioParaTablero(Guid EnvioId, string Numero, string Estado, DateOnly? FechaEntregaProgramada,
    Guid? FranjaHorariaId, bool EntregaPendiente, bool Entregado, int BultosEntregados, int BultosTotal);
