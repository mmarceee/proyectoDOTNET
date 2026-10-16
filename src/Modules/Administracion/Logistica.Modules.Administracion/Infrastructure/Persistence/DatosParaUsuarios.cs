using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Domain.Comercios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// Pasa por el filtro "Tenant": las relaciones y los repartidores son sólo los del operador de la sesión. Comercio no
// tiene filtro, pero se llega a él a través de la relación (ADR-0002, sección 2.6).
internal sealed class DatosParaUsuarios(AdministracionDbContext db) : IDatosParaUsuarios
{
    public async Task<IReadOnlyList<ComercioDelOperador>> ComerciosAsync(CancellationToken ct)
    {
        return await (
                from r in db.RelacionesComerciales.AsNoTracking()
                join c in db.Comercios.AsNoTracking() on r.ComercioId equals c.Id
                orderby c.RazonSocial
                select new ComercioDelOperador(c.Id, c.RazonSocial, r.Estado == EstadoRelacion.Activa))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RepartidorSinUsuario>> RepartidoresSinUsuarioAsync(CancellationToken ct)
    {
        return await db.Repartidores.AsNoTracking()
            .Where(r => r.Activo && r.UsuarioId == null)
            .OrderBy(r => r.Nombre)
            .Select(r => new RepartidorSinUsuario(r.Id, r.Nombre, r.Documento))
            .ToListAsync(ct);
    }
}
