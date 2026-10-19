using Logistica.Modules.Administracion.Domain.Planificacion;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// CU-04. No filtra por operador a mano: el filtro "Tenant" de ModuleDbContext ya limita todo al de la sesión.
// Las consultas tienen seguimiento de cambios, porque el handler modifica lo que trae y después guarda.
internal sealed class ZonaRepository(AdministracionDbContext db) : IZonaRepository
{
    public async Task<IReadOnlyList<Zona>> ListarAsync(CancellationToken ct)
    {
        return await db.Zonas.ToListAsync(ct);
    }

    public Task<Zona?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        return db.Zonas.SingleOrDefaultAsync(z => z.Id == id, ct);
    }

    public Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct)
    {
        return db.Zonas.AnyAsync(z => z.Codigo == codigo, ct);
    }

    public async Task<IReadOnlyList<FranjaHoraria>> FranjasDeAsync(Guid zonaId, CancellationToken ct)
    {
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

    public Task GuardarCambiosAsync(CancellationToken ct)
    {
        return db.SaveChangesAsync(ct);
    }
}
