namespace Logistica.Modules.Planificacion.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el cierre diario (CU-70, paso 3) encontró rutas del día, o de días
// anteriores, que no se finalizaron. Lo espera Seguimiento, para mostrar la alerta en el tablero del despachador (CU-66).
public sealed record RutasSinFinalizar(DateOnly Fecha, IReadOnlyList<Guid> RutaIds)
{
    public const string Tipo = "RutasSinFinalizar.v1";
}
