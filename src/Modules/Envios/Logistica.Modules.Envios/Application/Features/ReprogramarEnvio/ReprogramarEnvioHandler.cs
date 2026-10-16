using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.ReprogramarEnvio;

// CU-19, reprogramación manual (pasos 3 y 4). Coordina: busca el envío, pide la franja y la zona horaria a
// Administración por su contrato (ADR-0001) y calcula cuándo empieza la franja ese día. La regla A1 la decide Envio.
internal sealed class ReprogramarEnvioHandler(
    IEnvioRepository envios, IAdministracionModuleApi administracion, TimeProvider reloj)
{
    // false: el envío no existe o es de otro operador; el endpoint responde 404 (ADR-0002, sección 2.9).
    public async Task<bool> HandleAsync(ReprogramarEnvioCommand command, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(command.EnvioId, ct);
        if (envio is null)
        {
            return false;
        }

        // La franja tiene que aplicar esa fecha (día de la semana y vigencia) y ser de la zona del envío.
        var franja = (await administracion.ResolverFranjasAsync([command.FranjaId], command.Fecha, ct)).SingleOrDefault();
        if (franja is null || franja.ZonaId != envio.ZonaId)
        {
            throw new DomainException("Esa franja no está disponible ese día en la zona del envío.");
        }

        // Cuándo empieza la franja ese día, en la zona horaria del operador (las 14:00 de Montevideo son las 17:00 UTC).
        var contexto = await administracion.ObtenerContextoPlanificacionAsync(ct);
        var zonaHoraria = TimeZoneInfo.FindSystemTimeZoneById(contexto.ZonaHoraria);
        var horaLocal = command.Fecha.ToDateTime(franja.Desde);
        var comienzo = new DateTimeOffset(horaLocal, zonaHoraria.GetUtcOffset(horaLocal));

        // A1, en el dominio: no antes del plazo entre intentos.
        envio.ElegirNuevoIntento(command.Fecha, franja.Id, comienzo);

        // El aviso al destinatario va por el Outbox, en la misma transacción que la nueva fecha (ADR-0003).
        envios.RegistrarReprogramacion(envio, reloj.GetUtcNow());
        await envios.GuardarCambiosAsync(ct);
        return true;
    }
}
