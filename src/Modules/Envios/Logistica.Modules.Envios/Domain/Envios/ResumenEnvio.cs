namespace Logistica.Modules.Envios.Domain.Envios;

// Resumen del envío derivado de los estados de sus bultos (RF 31; decisiones-entrega-parcial.md, sección 2).
// Nadie lo asigna: lo calcula Envio.CalcularResumen al final de cada operación.
internal enum ResumenEnvio
{
    Creado,
    EnOperacion,
    EntregadoParcialmente,
    Entregado,
}
