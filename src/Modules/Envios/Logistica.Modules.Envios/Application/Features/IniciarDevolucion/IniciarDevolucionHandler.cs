using Logistica.Modules.Envios.Domain.Devoluciones;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Planificacion.Contracts;

namespace Logistica.Modules.Envios.Application.Features.IniciarDevolucion;

// CU-20: el despachador o el comercio deciden devolver un envío al comercio (T12 o T14).
// Coordina los pasos; el motivo, los estados permitidos y la regla del comercio los decide Envio.
internal sealed class IniciarDevolucionHandler(IEnvioRepository envios, IPlanificacionModuleApi planificacion,
    TimeProvider reloj)
{
    // false: el envío no existe o es de otro inquilino (o de otro comercio). Para quien pregunta es lo mismo
    // (ADR-0002, sección 2.9).
    public async Task<bool> HandleAsync(IniciarDevolucionCommand command, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(command.EnvioId, ct);
        if (envio is null)
        {
            return false;
        }

        var ahora = reloj.GetUtcNow();
        var anterior = envio.Estado; // después de IniciarDevolucion ya es EnDevolucion; el evento necesita el de antes

        // Motivo vacío, A1 (estado que no lo permite) y A2 (el comercio fuera del depósito) lanzan DomainException.
        envio.IniciarDevolucion(command.Motivo, command.Origen, command.ResponsableId, ahora);

        // Requerimiento especial 1: la Devolucion es otro agregado y se crea en la misma transacción.
        var devolucion = Devolucion.Crear(envio, command.Motivo, ahora);

        // CU-31 sólo entrega al comercio una devolución recibida en el depósito. En T12 el paquete ya está ahí; en T14
        // también, salvo que siga arriba del vehículo: en ese caso la marca la rendición (CU-55) cuando vuelve, como en
        // CU-18.
        if (anterior == EstadoEnvio.EnDeposito || !await planificacion.EnvioEnLaCalleAsync(envio.Id, ct))
        {
            devolucion.MarcarRecibida(ahora);
        }

        envios.Agregar(devolucion);

        // DevolucionIniciada va al Outbox, también en la misma transacción (ADR-0003).
        envios.RegistrarDevolucion(envio, devolucion, anterior, command.ResponsableId, command.Origen, ahora);

        // Un único guardado: el estado, la devolución y el mensaje se escriben juntos o no se escribe ninguno.
        await envios.GuardarCambiosAsync(ct);
        return true;
    }
}
