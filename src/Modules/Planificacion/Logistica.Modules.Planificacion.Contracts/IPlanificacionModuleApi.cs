using Logistica.Modules.Planificacion.Contracts.Results;

namespace Logistica.Modules.Planificacion.Contracts;

// Lo que Planificación ofrece a los demás módulos, en memoria (ADR-0001, addendum 3). Los otros módulos nunca
// tocan sus tablas ni sus entidades.
public interface IPlanificacionModuleApi
{
    // Incluye Finalizada para recibir posiciones capturadas antes de rendir, pendientes de sincronización.
    Task<RutaParaPosicion?> ConsultarParaPosicionAsync(Guid rutaId, Guid repartidorId, CancellationToken ct);
    Task<IReadOnlyList<RutaDelRepartidor>> ListarDelRepartidorAsync(Guid repartidorId, int pagina, CancellationToken ct);
    Task<RutaParaHoja?> ConsultarHojaAsync(Guid rutaId, Guid repartidorId, CancellationToken ct);
    Task<RutaParaCarga?> ConsultarCargaAsync(Guid rutaId, Guid repartidorId, CancellationToken ct);
    Task RegistrarCargaAsync(Guid rutaId, bool tieneEnviosCargados, CancellationToken ct);

    // CU-55: confirmada la rendición, la ruta queda Finalizada y deja libres al repartidor y al vehículo.
    // Sólo los envíos entregados en esta ruta quedan Completada; el resto queda Fallida (sin entrega).
    // DomainException si la ruta no existe, es de otro operador o no está en ejecución.
    Task FinalizarRutaAsync(Guid rutaId, IReadOnlyCollection<Guid> entregados, CancellationToken ct);

    // ADR-0004, sección 2.3, paso 1: la ruta es del repartidor, ya salió (Despachada o EnCurso) y el envío está en
    // una de sus paradas. false también si la ruta no existe o es de otro operador.
    Task<bool> EnvioEnRutaDelRepartidorAsync(Guid rutaId, Guid repartidorId, Guid envioId, CancellationToken ct);

    // CU-04: ¿alguno de estos envíos está en una ruta Planificada, Despachada o EnCurso con fecha igual o posterior?
    // Administración lo pregunta antes de reemplazar una franja, con el bloqueo de planificación del operador tomado,
    // para que nadie asigne esos envíos a una ruta mientras tanto.
    Task<bool> HayRutasActivasConEnviosAsync(IReadOnlyCollection<Guid> envioIds, DateOnly desde, CancellationToken ct);

    // CU-66: las rutas del operador que están en la calle (Despachada o EnCurso), para el tablero en vivo.
    Task<IReadOnlyList<RutaEnCurso>> ListarEnCursoAsync(CancellationToken ct);
}
