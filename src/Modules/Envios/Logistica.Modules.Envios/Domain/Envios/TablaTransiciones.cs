namespace Logistica.Modules.Envios.Domain.Envios;

// Transiciones permitidas entre estados del envío (tabla-de-transiciones-de-estados.md).
// Cualquier par que no esté acá se rechaza. T1 no figura porque no tiene estado de origen:
// la aplica Envio.Crear. T11 se eliminó.
internal static class TablaTransiciones
{
    private static readonly HashSet<(EstadoEnvio Origen, EstadoEnvio Destino)> Permitidas =
    [
        (EstadoEnvio.Admitido, EstadoEnvio.EnDeposito),         // T2  recepción en depósito
        (EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta),    // T3  asignar a una ruta
        (EstadoEnvio.AsignadoARuta, EstadoEnvio.EnDeposito),    // T4  quitar de la ruta
        (EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito),    // T5  confirmar la carga
        (EstadoEnvio.EnTransito, EstadoEnvio.Entregado),        // T6  entrega
        (EstadoEnvio.EnTransito, EstadoEnvio.NoEntregado),      // T7  intento fallido
        (EstadoEnvio.EnTransito, EstadoEnvio.EnDeposito),       // T8  reintegro sin intento
        (EstadoEnvio.NoEntregado, EstadoEnvio.Reprogramado),    // T9  reprogramación
        (EstadoEnvio.NoEntregado, EstadoEnvio.EnDevolucion),    // T10 devolución tras el intento
        (EstadoEnvio.EnDeposito, EstadoEnvio.EnDevolucion),     // T12 devolución desde depósito
        (EstadoEnvio.Reprogramado, EstadoEnvio.AsignadoARuta),  // T13 asignar tras reprogramar
        (EstadoEnvio.Reprogramado, EstadoEnvio.EnDevolucion),   // T14 venció el plazo
        (EstadoEnvio.EnDevolucion, EstadoEnvio.Devuelto),       // T15 el comercio lo recibió
        (EstadoEnvio.EnDeposito, EstadoEnvio.Extraviado),       // T16 extravío
        (EstadoEnvio.EnTransito, EstadoEnvio.Extraviado),       // T16
        (EstadoEnvio.EnDevolucion, EstadoEnvio.Extraviado),     // T16
        (EstadoEnvio.Admitido, EstadoEnvio.Cancelado),          // T17 cancelación
        (EstadoEnvio.EnDeposito, EstadoEnvio.Reprogramado),     // T18 pedido del destinatario
        (EstadoEnvio.AsignadoARuta, EstadoEnvio.Reprogramado),  // T19 pedido del destinatario
    ];

    public static bool Permite(EstadoEnvio origen, EstadoEnvio destino)
    {
        return Permitidas.Contains((origen, destino));
    }
}
