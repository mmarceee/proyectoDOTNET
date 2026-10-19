using Logistica.Modules.Planificacion.Contracts;
using Logistica.Modules.Planificacion.Contracts.Results;
using Logistica.Modules.Planificacion.Domain.Rutas;

namespace Logistica.Modules.Planificacion.Application;

// Implementación del contrato de Planificación. Es internal: los otros módulos sólo ven la interfaz.
internal sealed class PlanificacionModuleApi(IRutaRepository rutas) : IPlanificacionModuleApi
{
    public async Task<IReadOnlyList<RutaDelRepartidor>> ListarDelRepartidorAsync(Guid repartidorId, int pagina, CancellationToken ct)
    {
        if (pagina is < 1 or > 10000) throw new Logistica.SharedKernel.DomainException("La página no es válida.");
        return (await rutas.ListarDelRepartidorAsync(repartidorId, pagina, ct)).Select(r =>
            new RutaDelRepartidor(r.Id, r.Fecha, r.Estado.ToString(), r.VehiculoId, r.Paradas.Count, r.EnvioIds.ToArray())).ToArray();
    }

    public async Task<RutaParaHoja?> ConsultarHojaAsync(Guid rutaId, Guid repartidorId, CancellationToken ct)
    {
        var r = await rutas.ObtenerAsync(rutaId, ct);
        return r is null || r.RepartidorId != repartidorId || r.Estado is not (EstadoRuta.Despachada or EstadoRuta.EnCurso)
            ? null : new(r.Id, r.VehiculoId, r.Revision, r.Fecha, r.Estado.ToString(),
                r.Paradas.OrderBy(p => p.Orden).Select(p => new ParadaParaHoja(p.Id, p.Orden, p.Estado.ToString(),
                    p.Direccion, p.Desde, p.Hasta, p.Envios.Select(e => e.EnvioId).ToArray())).ToArray());
    }
    public async Task<RutaParaCarga?> ConsultarCargaAsync(Guid rutaId, Guid repartidorId, CancellationToken ct)
    {
        var ruta = await rutas.ObtenerAsync(rutaId, ct);
        return ruta is null || ruta.RepartidorId != repartidorId || ruta.Estado is not (EstadoRuta.Despachada or EstadoRuta.EnCurso)
            ? null : new(ruta.Revision, ruta.Estado.ToString(), ruta.EnvioIds.ToArray(), ruta.Fecha);
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
