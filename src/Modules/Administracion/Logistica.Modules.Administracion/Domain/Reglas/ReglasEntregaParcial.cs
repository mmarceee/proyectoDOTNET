using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Domain.Reglas;

// RF 33: las reglas de la entrega parcial de una versión (decisiones-entrega-parcial.md, sección 5).
// - AdmiteEntregaParcial: con no, en una visita se entregan todos los bultos o ninguno.
// - Pendientes: qué pasa con cada bulto que no se entregó.
// - RecargoNuevaVisita: monto fijo por visita adicional; sólo con la política NuevaVisitaConRecargo.
internal sealed record ReglasEntregaParcial(bool AdmiteEntregaParcial, PoliticaPendientes Pendientes,
    decimal RecargoNuevaVisita)
{
    // Las versiones publicadas antes de RF 33 se comportan como hasta ahora: todo o nada y revisita sin recargo
    // (decisión del 03/11/2026).
    public static readonly ReglasEntregaParcial ComoAntes = new(false, PoliticaPendientes.NuevaVisitaSinRecargo, 0);
}
