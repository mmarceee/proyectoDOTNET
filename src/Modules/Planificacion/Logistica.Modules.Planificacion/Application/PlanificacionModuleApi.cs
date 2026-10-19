using Logistica.Modules.Planificacion.Contracts;
using Logistica.Modules.Planificacion.Contracts.Results;
using Logistica.Modules.Planificacion.Domain.Rutas;

namespace Logistica.Modules.Planificacion.Application;

// Implementación del contrato de Planificación. Es internal: los otros módulos sólo ven la interfaz.
internal sealed class PlanificacionModuleApi(IRutaRepository rutas) : IPlanificacionModuleApi
{
    public async Task<RutaParaCarga?> ConsultarCargaAsync(Guid rutaId, Guid repartidorId, CancellationToken ct)
    {
        var ruta = await rutas.ObtenerAsync(rutaId, ct);
        return ruta is null || ruta.RepartidorId != repartidorId || ruta.Estado is not (EstadoRuta.Despachada or EstadoRuta.EnCurso)
            ? null : new(ruta.Revision, ruta.Estado.ToString(), ruta.EnvioIds.ToArray());
    }

    public async Task RegistrarCargaAsync(Guid rutaId, bool tieneEnviosCargados, CancellationToken ct)
    {
        var ruta = await rutas.ObtenerAsync(rutaId, ct)
            ?? throw new Logistica.SharedKernel.DomainException("La ruta no está disponible.");
        ruta.RegistrarCarga(tieneEnviosCargados);
        await rutas.GuardarAsync(ct);
    }

    public async Task<bool> EnvioEnRutaDelRepartidorAsync(Guid rutaId, Guid repartidorId, Guid envioId, CancellationToken ct)
    {
        // El filtro "Tenant" deja afuera las rutas de otros operadores: para quien pregunta, no existen.
        var ruta = await rutas.ObtenerAsync(rutaId, ct);

        return ruta is not null
            && ruta.RepartidorId == repartidorId
            && ruta.Estado is EstadoRuta.Despachada or EstadoRuta.EnCurso
            && ruta.EnvioIds.Contains(envioId);
    }
}
