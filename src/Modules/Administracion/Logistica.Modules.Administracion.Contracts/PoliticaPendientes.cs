namespace Logistica.Modules.Administracion.Contracts;

// RF 33: qué pasa con un bulto que no se entregó en una visita (decisiones-entrega-parcial.md, sección 5). La define
// Administración en sus reglas versionadas y Envíos la aplica desde acá (guía, sección 2.8, regla 2).
public enum PoliticaPendientes
{
    // Vuelve a visitarse sin costo extra (T9, si el motivo es reprogramable y le quedan intentos). Es lo de antes de RF 33.
    NuevaVisitaSinRecargo,
    // Vuelve a visitarse y la visita adicional lleva un recargo fijo.
    NuevaVisitaConRecargo,
    // Pasa al circuito de devolución (T10) aunque le queden intentos.
    Devolver,
}
