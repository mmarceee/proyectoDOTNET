using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// Lo que la versión de reglas del envío exige al entregarlo o al registrar un intento fallido (letra, sección 6.6;
// modelo de dominio, VersionReglas y PruebaExigida). Las reglas viven en Administración: Envíos recibe esta copia
// para decidir. La firma no figura porque no se configura: es siempre obligatoria (la valida IntentoEntrega).
internal sealed record ReglasEntrega
{
    public bool ExigeFoto { get; }
    public bool ExigeNombreYDocumento { get; }
    public int MaxIntentos { get; }
    public TimeSpan PlazoEntreIntentos { get; }

    // Política de devolución (modelo de dominio, PoliticaDevolucion): al agotar los intentos, true inicia la devolución
    // sola (T10); false deja el envío Reprogramado para que el despachador decida (CU-19, A2, y CU-20).
    public bool DevolverTrasIntentosAgotados { get; }

    // RF 33: si una visita puede entregar parte de los bultos. Falso por defecto, como en las versiones ya publicadas
    // (decisiones-entrega-parcial.md, sección 5): sin parcial, se entregan todos los bultos o ninguno.
    public bool AdmiteEntregaParcial { get; }

    // RF 33: qué pasa con un bulto no entregado. Con Devolver va a devolución (T10) aunque le queden intentos; con las
    // otras dos sigue la regla de siempre (T9 o T10). El recargo de la nueva visita se registra en otra etapa.
    public PoliticaPendientes Pendientes { get; }
    public decimal RecargoNuevaVisita { get; }

    public ReglasEntrega(bool exigeFoto, bool exigeNombreYDocumento, int maxIntentos, TimeSpan plazoEntreIntentos,
        bool devolverTrasIntentosAgotados = true, bool admiteEntregaParcial = false,
        PoliticaPendientes pendientes = PoliticaPendientes.NuevaVisitaSinRecargo, decimal recargoNuevaVisita = 0)
    {
        if (maxIntentos < 1)
        {
            throw new DomainException("El máximo de intentos de entrega debe ser al menos 1.");
        }

        if (plazoEntreIntentos < TimeSpan.Zero)
        {
            throw new DomainException("El plazo entre intentos no puede ser negativo.");
        }

        ExigeFoto = exigeFoto;
        ExigeNombreYDocumento = exigeNombreYDocumento;
        MaxIntentos = maxIntentos;
        PlazoEntreIntentos = plazoEntreIntentos;
        DevolverTrasIntentosAgotados = devolverTrasIntentosAgotados;
        AdmiteEntregaParcial = admiteEntregaParcial;
        Pendientes = pendientes;
        RecargoNuevaVisita = recargoNuevaVisita;
    }

    // T9 o T10 después de un intento fallido (CU-18; RF 33). Devuelve un motivo no reprogramable, la política de
    // pendientes Devolver, o los intentos agotados si la política de devolución lo dice.
    public bool DebeDevolver(MotivoNoEntrega motivo, int intentosFallidos) =>
        !motivo.Reprogramable
        || Pendientes == PoliticaPendientes.Devolver
        || (intentosFallidos >= MaxIntentos && DevolverTrasIntentosAgotados);
}
