using Logistica.Modules.Administracion.Domain.Operadores;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class OperadorRepository(AdministracionDbContext db) : IOperadorRepository
{
    public Task<Operador?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        return db.Operadores.SingleOrDefaultAsync(o => o.Id == id, ct);
    }

    public Task<LogoOperador?> ObtenerLogoAsync(Guid id, CancellationToken ct)
    {
        return db.Logos.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct);
    }

    public void AgregarLogo(LogoOperador logo)
    {
        db.Logos.Add(logo);
    }

    public Task GuardarCambiosAsync(CancellationToken ct)
    {
        return db.SaveChangesAsync(ct);
    }
}
