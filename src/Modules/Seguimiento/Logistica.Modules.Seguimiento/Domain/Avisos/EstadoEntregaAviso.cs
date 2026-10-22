namespace Logistica.Modules.Seguimiento.Domain.Avisos;

// Pendiente: todavía sin intentos. Fallida: el último intento falló y quedan reintentos.
// EnColaDeFallidos: se agotaron los reintentos; CU-65 o CU-09 la vuelven a encolar.
internal enum EstadoEntregaAviso
{
    Pendiente,
    Exitosa,
    Fallida,
    EnColaDeFallidos
}
