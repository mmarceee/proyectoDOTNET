using Logistica.Modules.Administracion.Domain;
using Logistica.Modules.Administracion.Domain.Tarifas;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// CU-05. No filtra por operador a mano: el filtro "Tenant" ya limita todo al de la sesión. Cada lectura se suma a la
// transacción en curso o se desengancha de una que terminó, igual que ZonaRepository.
internal sealed class TarifarioRepository(AdministracionDbContext db) : ITarifarioRepository
{
    private IQueryable<VersionTarifario> ConDetalle => db.VersionesTarifario.Include(v => v.Reglas).Include(v => v.Ajustes);

    public async Task<IReadOnlyList<VersionTarifario>> ListarAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.VersionesTarifario.AsNoTracking().OrderByDescending(v => v.Numero).ToListAsync(ct);
    }

    public async Task<VersionTarifario?> ObtenerBorradorAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConDetalle.SingleOrDefaultAsync(v => v.Estado == EstadoVersion.Borrador, ct);
    }

    public async Task<VersionTarifario?> UltimaPublicadaAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConDetalle.Where(v => v.Estado != EstadoVersion.Borrador)
            .OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);
    }

    public async Task<int> SiguienteNumeroAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return (await db.VersionesTarifario.MaxAsync(v => (int?)v.Numero, ct) ?? 0) + 1;
    }

    public async Task<VersionTarifario?> VigenteEnAsync(DateTimeOffset momento, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConDetalle.AsNoTracking()
            .Where(v => v.Estado != EstadoVersion.Borrador && v.VigenteDesde <= momento
                && (v.VigenteHasta == null || momento < v.VigenteHasta))
            .OrderByDescending(v => v.Numero).FirstOrDefaultAsync(ct);
    }

    public async Task<DateTimeOffset?> ProximoInicioAsync(DateTimeOffset momento, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.VersionesTarifario
            .Where(v => v.Estado != EstadoVersion.Borrador && v.VigenteDesde > momento)
            .MinAsync(v => v.VigenteDesde, ct);
    }

    public async Task<VersionTarifario?> ObtenerPorNumeroAsync(int numero, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await ConDetalle.AsNoTracking().SingleOrDefaultAsync(v => v.Numero == numero, ct);
    }

    public void Agregar(VersionTarifario version)
    {
        db.VersionesTarifario.Add(version);
    }

    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_VersionesTarifario_Borrador" })
        {
            // A4 con dos "Nueva versión" a la vez: el índice parcial frena el segundo borrador.
            throw new DomainException("Ya hay un borrador en curso: abrilo para seguir editándolo.");
        }
        catch (DbUpdateConcurrencyException)
        {
            // A5: otro administrador publicó o cambió esta versión desde que la abriste.
            throw new DomainException("La versión cambió mientras la editabas. Revisá el borrador y volvé a intentar.");
        }
    }
}
