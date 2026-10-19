using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// CU-04. No filtra por operador a mano: el filtro "Tenant" de ModuleDbContext ya limita todo al de la sesión.
// Las consultas tienen seguimiento de cambios, porque el handler modifica lo que trae y después guarda.
// Cada lectura se suma a la transacción en curso o, si terminó, se desengancha de ella: después de un reemplazo de
// franja rechazado, la página vuelve a leer con el mismo DbContext.
internal sealed class ZonaRepository(AdministracionDbContext db) : IZonaRepository
{
    public async Task<IReadOnlyList<Zona>> ListarAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Zonas.ToListAsync(ct);
    }

    public async Task<Zona?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Zonas.SingleOrDefaultAsync(z => z.Id == id, ct);
    }

    public async Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Zonas.AnyAsync(z => z.Codigo == codigo, ct);
    }

    public async Task<IReadOnlyList<FranjaHoraria>> FranjasDeAsync(Guid zonaId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Franjas.Where(f => f.ZonaId == zonaId).ToListAsync(ct);
    }

    public void Agregar(Zona zona)
    {
        db.Zonas.Add(zona);
    }

    public void AgregarFranja(FranjaHoraria franja)
    {
        db.Franjas.Add(franja);
    }

    // A1 con dos altas simultáneas: las dos pasan la consulta previa, y el índice único frena a la segunda. Se informa
    // igual que la consulta previa, en lugar de un error 500.
    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Zonas_OperadorId_Codigo" })
        {
            throw new DomainException("Ya existe una zona con ese código.");
        }
    }
}
