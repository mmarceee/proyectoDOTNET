using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence;

internal sealed class RutaRepository(PlanificacionDbContext db) : IRutaRepository
{
    // El filtro global Tenant protege raíces, paradas y evidencia, incluidas sus navegaciones.
    private IQueryable<Ruta> Consulta => db.Rutas;
    public async Task<IReadOnlyList<Ruta>> ListarDelRepartidorAsync(Guid repartidor, int pagina, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await Consulta.AsNoTracking().Include(r => r.Paradas).ThenInclude(p => p.Envios)
            .Where(r => r.RepartidorId == repartidor && r.Estado != EstadoRuta.Planificada)
            .OrderBy(r => r.Estado == EstadoRuta.Finalizada)
            .ThenBy(r => r.Estado == EstadoRuta.Finalizada ? DateOnly.MinValue : r.Fecha)
            .ThenByDescending(r => r.Fecha).ThenBy(r => r.Id)
            .Skip((pagina - 1) * 20).Take(20).ToListAsync(ct);
    }
    public async Task<Ruta?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await Consulta.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleOrDefaultAsync(r => r.Id == id, ct);
    }
    public async Task<IReadOnlyList<Ruta>> ListarAsync(DateOnly? fecha, EstadoRuta? estado, CancellationToken ct)
        => await Consulta.AsNoTracking().Include(r => r.Paradas).ThenInclude(p => p.Envios)
            .Where(r => (fecha == null || r.Fecha == fecha) && (estado == null || r.Estado == estado))
            .OrderByDescending(r => r.Fecha).ThenBy(r => r.Id).Take(100).ToListAsync(ct);
    public async Task<IReadOnlyList<Guid>> EnviosOcupadosAsync(IReadOnlyCollection<Guid> ids, Guid? propia, CancellationToken ct)
        => await db.EnviosParada.AsNoTracking().Where(p => ids.Contains(p.EnvioId)
            && p.Estado == EstadoParada.Pendiente && p.RutaId != propia).Select(p => p.EnvioId).ToListAsync(ct);
    public async Task<IReadOnlyList<string>> RecursosOcupadosAsync(DateOnly fecha, Guid repartidor, Guid vehiculo, Guid? propia, CancellationToken ct)
    {
        var rutas = await Consulta.AsNoTracking().Where(r => r.Id != propia && r.ReservaActiva && (r.Fecha == fecha
            || r.Fecha < fecha && (r.Estado == EstadoRuta.Despachada || r.Estado == EstadoRuta.EnCurso)))
            .Where(r => r.RepartidorId == repartidor || r.VehiculoId == vehiculo).ToListAsync(ct);
        var resultado = new List<string>();
        if (rutas.Any(r => r.RepartidorId == repartidor)) resultado.Add("RepartidorOcupado");
        if (rutas.Any(r => r.VehiculoId == vehiculo)) resultado.Add("VehiculoOcupado");
        return resultado;
    }
    public async Task<IReadOnlyList<ValidacionRuta>> ValidacionesAsync(Guid rutaId, int pagina, CancellationToken ct)
        => await db.Validaciones.AsNoTracking().Include(v => v.Bultos).Where(v => v.RutaId == rutaId)
            .OrderByDescending(v => v.RevisionRuta).Skip((pagina - 1) * 20).Take(20).ToListAsync(ct);
    public void Agregar(Ruta ruta) => db.Rutas.Add(ruta);
    public async Task<IReadOnlyList<RetiroEnvioRuta>> RetirosAsync(Guid rutaId, int pagina, CancellationToken ct)
        => await db.Retiros.AsNoTracking().Where(r => r.RutaId == rutaId).OrderByDescending(r => r.RevisionRuta)
            .Skip((pagina - 1) * 20).Take(20).ToListAsync(ct);
    public void Agregar(RetiroEnvioRuta retiro) => db.Retiros.Add(retiro);
    public void Agregar(ValidacionRuta validacion) => db.Validaciones.Add(validacion);
    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
