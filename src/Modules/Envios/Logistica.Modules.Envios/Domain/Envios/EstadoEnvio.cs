namespace Logistica.Modules.Envios.Domain.Envios;

// Estados del envío (modelo de dominio; transiciones en la tabla T1 a T19).
internal enum EstadoEnvio
{
    Admitido,
    EnDeposito,
    AsignadoARuta,
    EnTransito,
    Entregado,
    NoEntregado,
    Reprogramado,
    EnDevolucion,
    Devuelto,
    Extraviado,
    Cancelado,
}
