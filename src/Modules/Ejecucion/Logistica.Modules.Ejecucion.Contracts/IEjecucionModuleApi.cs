using Logistica.Modules.Ejecucion.Contracts.Results;

namespace Logistica.Modules.Ejecucion.Contracts;

// CU-55 consulta al dueño de Rendicion. El operador siempre proviene de la sesión.
public interface IEjecucionModuleApi
{
    // CU-66: hasta 500 vehículos del operador de la sesión; omite los que no tienen posición vigente.
    Task<IReadOnlyList<PosicionVehiculoResult>> ConsultarUltimasPosicionesAsync(IReadOnlyCollection<Guid> vehiculos, CancellationToken ct);
    // CU-43: incluye cargas sin envíos completos, escaneos y rendiciones. No ve colas sin sincronizar.
    Task<bool> TieneActividadRegistradaAsync(Guid rutaId, CancellationToken ct);
    Task<RendicionPendiente?> ConsultarRendicionAsync(Guid rutaId, CancellationToken ct);
    Task<IReadOnlyList<RendicionPendiente>> ListarRendicionesPendientesAsync(int pagina, CancellationToken ct);

    // CU-55: registra los bultos que recibió el operario y cierra la rendición. Devuelve cómo quedó cada línea, para que
    // Depósito reintegre los envíos (T8) y registre los faltantes en la misma transacción. null si la ruta no tiene
    // rendición; DomainException si ya se confirmó o si se recibió un bulto que no tenía que volver.
    Task<RendicionPendiente?> ConfirmarRendicionAsync(Guid rutaId, IReadOnlyCollection<Guid> bultosRecibidos,
        CancellationToken ct);
}
