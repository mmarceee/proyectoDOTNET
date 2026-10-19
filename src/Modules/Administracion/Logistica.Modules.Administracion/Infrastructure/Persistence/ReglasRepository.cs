using Logistica.Modules.Administracion.Domain;
using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// CU-06. Igual que TarifarioRepository: sin filtrar por operador a mano (lo hace el filtro "Tenant") y cada lectura se
// suma a la transacción en curso o se desengancha de una que terminó.
internal sealed class ReglasRepository(AdministracionDbContext db) : IReglasRepository
{
    private IQueryable<VersionReglas> ConMotivos => db.VersionesReglas.Include(v => v.Motivos);

    public async Task<IReadOnlyList<VersionReglas>> ListarAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.VersionesReglas.AsNoTracking().OrderByDescending(v => v.Numero).ToListAsync(ct);
    }

    public async Task<VersionReglas?> ObtenerBorradorAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConMotivos.SingleOrDefaultAsync(v => v.Estado == EstadoVersion.Borrador, ct);
    }

    public async Task<VersionReglas?> UltimaPublicadaAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConMotivos.Where(v => v.Estado != EstadoVersion.Borrador)
            .OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);
    }

    public async Task<int> SiguienteNumeroAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return (await db.VersionesReglas.MaxAsync(v => (int?)v.Numero, ct) ?? 0) + 1;
    }

    public async Task<VersionReglas?> VigenteEnAsync(DateTimeOffset momento, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConMotivos.AsNoTracking()
            .Where(v => v.Estado != EstadoVersion.Borrador && v.VigenteDesde <= momento
                && (v.VigenteHasta == null || momento < v.VigenteHasta))
            .OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);
    }

    public async Task<DateTimeOffset?> ProximoInicioAsync(DateTimeOffset momento, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.VersionesReglas
            .Where(v => v.Estado != EstadoVersion.Borrador && v.VigenteDesde > momento)
            .MinAsync(v => v.VigenteDesde, ct);
    }

    public async Task<VersionReglas?> ObtenerPorNumeroAsync(int numero, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConMotivos.AsNoTracking().SingleOrDefaultAsync(v => v.Numero == numero, ct);
    }

    // Para el contrato de Envíos: las reglas con que se admitió un envío. Una de otro operador no se encuentra.
    public async Task<VersionReglas?> ObtenerAsync(Guid versionId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConMotivos.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == versionId && v.Estado != EstadoVersion.Borrador, ct);
    }

    public void Agregar(VersionReglas version)
    {
        db.VersionesReglas.Add(version);
    }

    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_VersionesReglas_Borrador" })
        {
            throw new DomainException("Ya hay un borrador en curso: abrilo para seguir editándolo.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("La versión cambió mientras la editabas. Revisá el borrador y volvé a intentar.");
        }
    }
}
