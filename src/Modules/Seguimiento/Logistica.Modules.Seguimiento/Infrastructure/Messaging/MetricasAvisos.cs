using System.Diagnostics.Metrics;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// Avisos a comercios fallidos para el tablero técnico (letra, sección 6.12; CU-64). Cada intento que el receptor no
// acepta suma a intentos_fallidos; el que además agota los reintentos y deja la entrega en la cola de fallidos suma a
// fallidos. El Worker, que entrega los avisos, registra este medidor.
internal static class MetricasAvisos
{
    private static readonly Meter Origen = new(SeguimientoModule.Medidor);

    private static readonly Counter<long> IntentosFallidos = Origen.CreateCounter<long>(
        "logistica.avisos.intentos_fallidos", "{intento}", "Intentos de entrega de un aviso que el comercio no aceptó.");

    private static readonly Counter<long> Fallidos = Origen.CreateCounter<long>(
        "logistica.avisos.fallidos", "{aviso}", "Avisos que agotaron los reintentos y quedaron en la cola de fallidos.");

    public static void IntentoFallido(bool reintentosAgotados)
    {
        IntentosFallidos.Add(1);
        if (reintentosAgotados)
        {
            Fallidos.Add(1);
        }
    }
}
