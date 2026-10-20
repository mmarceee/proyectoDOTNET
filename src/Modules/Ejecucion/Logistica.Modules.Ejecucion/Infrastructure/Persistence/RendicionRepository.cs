using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence;

internal sealed class RendicionRepository(EjecucionDbContext db) : IRendicionRepository
{
    public async Task<Rendicion?> ObtenerAsync(Guid rutaId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Rendiciones.Include(r => r.Lineas).SingleOrDefaultAsync(r => r.RutaId == rutaId, ct);
    }
    public async Task<IReadOnlyList<Rendicion>> PendientesAsync(int pagina, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Rendiciones.AsNoTracking().Include(r => r.Lineas).Where(r => r.FinalizadaEn == null)
            .OrderBy(r => r.IniciadaEn).ThenBy(r => r.Id).Skip((pagina - 1) * 20).Take(20).ToListAsync(ct);
    }
    public async Task<EvidenciaRuta> EvidenciaAsync(Guid rutaId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        var bultos = await db.EscaneosCarga.Where(e => e.RutaId == rutaId && e.Resultado == "Esperado" && e.BultoId != null)
            .Select(e => e.BultoId!.Value).Distinct().ToListAsync(ct);
        // No traer firmas, fotos ni el payload rechazado para preparar la rendición.
        var ops = await db.Operaciones.AsNoTracking().Where(o => o.RutaId == rutaId)
            .Select(o => new OperacionRuta(o.Id, o.RepartidorId, o.EnvioId, o.Tipo, o.Resultado, o.Revisada)).ToListAsync(ct);
        return new(bultos.ToHashSet(), ops.Where(o => o.Resultado == ResultadoOperacion.Aplicada && o.Tipo == "RegistrarIntentoFallido"
                && o.EnvioId != null).Select(o => o.EnvioId!.Value).ToHashSet(), ops);
    }
    public async Task<bool> TieneActividadRegistradaAsync(Guid rutaId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Rendiciones.AnyAsync(r => r.RutaId == rutaId, ct)
            || await db.EscaneosCarga.AnyAsync(e => e.RutaId == rutaId, ct)
            || await db.Operaciones.AnyAsync(o => o.RutaId == rutaId && o.Resultado == ResultadoOperacion.Aplicada, ct);
    }
    public void Agregar(Rendicion rendicion) => db.Rendiciones.Add(rendicion);
    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
