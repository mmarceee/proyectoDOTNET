using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;

namespace Logistica.Modules.Envios.Application.Features.DeclararExtravio;

// CU-21: el administrador o el despachador declara perdido un envío (T16).
// Coordina los pasos; el motivo obligatorio y los estados permitidos los decide Envio con la tabla de transiciones.
internal sealed class DeclararExtravioHandler(IEnvioRepository envios, TimeProvider reloj)
{
    // false: el envío no existe o es de otro inquilino. Para quien pregunta es lo mismo (ADR-0002, sección 2.9).
    public async Task<bool> HandleAsync(DeclararExtravioCommand command, CancellationToken ct)
    {
        var id = await envios.IdPorNumeroAsync(command.Numero, ct);
        var envio = id is Guid envioId ? await envios.ObtenerAsync(envioId, ct) : null;
        if (envio is null)
        {
            return false;
        }

        var ahora = reloj.GetUtcNow();
        var anterior = envio.Estado; // después de DeclararExtravio ya es Extraviado; el evento necesita el de antes

        // T16. A1 (sin motivo) y A2 (estado que no lo permite) lanzan DomainException: el envío no cambia.
        envio.DeclararExtravio(command.Motivo, command.ResponsableId, ahora);

        // Incidencia de tipo Extravío: otro agregado, en la misma transacción (modelo de dominio).
        envios.Agregar(Incidencia.Crear(envio, TipoIncidencia.Extravio, command.Motivo, ahora));

        // EnvioExtraviado va al Outbox, también en la misma transacción (ADR-0003).
        envios.RegistrarExtravio(envio, anterior, command.Motivo, command.ResponsableId, ahora);

        // Un único guardado: el estado, la incidencia y el mensaje se escriben juntos o no se escribe ninguno.
        await envios.GuardarCambiosAsync(ct);
        return true;
    }
}
