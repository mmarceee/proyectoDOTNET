using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Application.Features.CancelarEnvio;

// CU-16: el comercio anula un envío que todavía no llegó al depósito (T17).
// Coordina los pasos; que sólo se pueda cancelar desde Admitido lo decide la tabla de transiciones.
internal sealed class CancelarEnvioHandler(IEnvioRepository envios, TimeProvider reloj)
{
    // false: el envío no existe o es de otro inquilino. Para quien pregunta es lo mismo (ADR-0002, sección 2.9).
    public async Task<bool> HandleAsync(CancelarEnvioCommand command, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(command.EnvioId, ct);
        if (envio is null)
        {
            return false;
        }

        var motivo = string.IsNullOrWhiteSpace(command.Motivo) ? null : command.Motivo.Trim();
        var ahora = reloj.GetUtcNow();

        // T17. Si ya no está Admitido (A1: lo recibieron en depósito mientras el usuario confirmaba),
        // Transicionar lanza TransicionInvalidaException y la API responde 400.
        envio.Transicionar(EstadoEnvio.Cancelado, OrigenEvento.PortalComercio, responsableId: null, ahora, detalle: motivo);

        // EnvioCancelado va al Outbox en la misma transacción que el cambio de estado (ADR-0003).
        envios.RegistrarCancelacion(envio, motivo, ahora);

        await envios.GuardarCambiosAsync(ct);
        return true;
    }
}
