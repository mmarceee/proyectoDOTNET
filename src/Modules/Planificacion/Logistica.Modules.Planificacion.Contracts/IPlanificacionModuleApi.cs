using Logistica.Modules.Planificacion.Contracts.Results;

namespace Logistica.Modules.Planificacion.Contracts;

// Lo que Planificación ofrece a los demás módulos, en memoria (ADR-0001, addendum 3). Los otros módulos nunca
// tocan sus tablas ni sus entidades.
public interface IPlanificacionModuleApi
{
    Task<RutaParaCarga?> ConsultarCargaAsync(Guid rutaId, Guid repartidorId, CancellationToken ct);
    Task RegistrarCargaAsync(Guid rutaId, bool tieneEnviosCargados, CancellationToken ct);

    // ADR-0004, sección 2.3, paso 1: la ruta es del repartidor, ya salió (Despachada o EnCurso) y el envío está en
    // una de sus paradas. false también si la ruta no existe o es de otro operador.
    Task<bool> EnvioEnRutaDelRepartidorAsync(Guid rutaId, Guid repartidorId, Guid envioId, CancellationToken ct);

    // CU-04: ¿alguno de estos envíos está en una ruta Planificada, Despachada o EnCurso con fecha igual o posterior?
    // Administración lo pregunta antes de reemplazar una franja, con el bloqueo de planificación del operador tomado,
    // para que nadie asigne esos envíos a una ruta mientras tanto.
    Task<bool> HayRutasActivasConEnviosAsync(IReadOnlyCollection<Guid> envioIds, DateOnly desde, CancellationToken ct);
}
