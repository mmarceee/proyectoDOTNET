using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;

namespace Logistica.Modules.Envios.Application.Features.GestionarIncidencias;

// CU-22: el despachador registra, atiende y resuelve las incidencias de un envío. Coordina los pasos; las reglas (tipo
// válido, descripción y resolución obligatorias, de qué estado a cuál) las decide Incidencia. Una incidencia no cambia
// el estado del envío.
// false en cada método: el envío o la incidencia no existe o es de otro inquilino. Para quien pregunta es lo mismo
// (ADR-0002, sección 2.9).
internal sealed class GestionarIncidenciasHandler(IEnvioRepository envios, TimeProvider reloj)
{
    // Pasos 1 a 3: queda Abierta.
    public async Task<bool> RegistrarAsync(RegistrarIncidenciaCommand command, CancellationToken ct)
    {
        var id = await envios.IdPorNumeroAsync(command.Numero, ct);
        var envio = id is Guid envioId ? await envios.ObtenerAsync(envioId, ct) : null;
        if (envio is null)
        {
            return false;
        }

        // Sin tipo válido o sin descripción, DomainException y no se guarda nada.
        envios.Agregar(Incidencia.Crear(envio, command.Tipo, command.Descripcion, reloj.GetUtcNow()));
        await envios.GuardarCambiosAsync(ct);
        return true;
    }

    // Paso 4.
    public async Task<bool> PasarAEnProcesoAsync(Guid incidenciaId, CancellationToken ct)
    {
        var incidencia = await envios.ObtenerIncidenciaAsync(incidenciaId, ct);
        if (incidencia is null)
        {
            return false;
        }

        incidencia.PasarAEnProceso();
        await envios.GuardarCambiosAsync(ct);
        return true;
    }

    // Paso 5. Sin resolución, DomainException (A2).
    public async Task<bool> ResolverAsync(Guid incidenciaId, string resolucion, CancellationToken ct)
    {
        var incidencia = await envios.ObtenerIncidenciaAsync(incidenciaId, ct);
        if (incidencia is null)
        {
            return false;
        }

        incidencia.Resolver(resolucion, reloj.GetUtcNow());
        await envios.GuardarCambiosAsync(ct);
        return true;
    }
}
