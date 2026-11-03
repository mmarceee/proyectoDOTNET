using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.Modules.Deposito.Infrastructure.Persistence.Configurations;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence;

internal sealed class LiquidacionRepository(DepositoDbContext db) : ILiquidacionRepository
{
    public async Task<IReadOnlySet<Guid>> EnviosLiquidadosAsync(IReadOnlyCollection<Guid> envioIds, CancellationToken ct)
    {
        if (envioIds.Count == 0) return new HashSet<Guid>();
        return (await db.LineasLiquidacion.Where(l => envioIds.Contains(l.EnvioId)).Select(l => l.EnvioId).ToListAsync(ct))
            .ToHashSet();
    }

    public async Task<IReadOnlyList<Liquidacion>> ListarAsync(CancellationToken ct)
    {
        return await db.Liquidaciones.AsNoTracking().OrderByDescending(l => l.GeneradaEn).ToListAsync(ct);
    }

    public Task<Liquidacion?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        return db.Liquidaciones.Include(l => l.Lineas).SingleOrDefaultAsync(l => l.Id == id, ct);
    }

    public void Agregar(Liquidacion liquidacion)
    {
        db.Liquidaciones.Add(liquidacion);
    }

    public void Eliminar(Liquidacion liquidacion)
    {
        db.Liquidaciones.Remove(liquidacion);
    }

    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new DomainException("La liquidación cambió mientras la revisabas. Volvé a abrirla."); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: LineaLiquidacionConfiguration.IndiceEnvio })
        { throw new DomainException("Otra liquidación incluyó alguno de estos envíos mientras se generaba. Volvé a generarla."); }
    }
}
