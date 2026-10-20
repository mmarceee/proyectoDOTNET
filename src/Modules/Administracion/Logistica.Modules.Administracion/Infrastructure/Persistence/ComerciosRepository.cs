using Logistica.Modules.Administracion.Application.Features.GestionarComercios;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class ComerciosRepository(AdministracionDbContext db, ICurrentTenant tenant) : IComerciosRepository
{
    private IQueryable<ComercioVista> Consulta =>
        from r in db.RelacionesComerciales.AsNoTracking()
        join c in db.Comercios.AsNoTracking() on r.ComercioId equals c.Id
        select new ComercioVista
        {
            Id = c.Id, RazonSocial = c.RazonSocial, DocumentoFiscal = c.DocumentoFiscal,
            EmailContacto = r.EmailContacto, Estado = r.Estado, FechaAlta = r.FechaAlta, Version = r.Version
        };

    public async Task<IReadOnlyList<ComercioVista>> ListarAsync(string? buscar, EstadoRelacion? estado, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        var consulta = Consulta;
        if (!string.IsNullOrWhiteSpace(buscar))
            consulta = consulta.Where(c => c.RazonSocial.ToLower().Contains(buscar.ToLower()) || c.DocumentoFiscal.Contains(buscar));
        if (estado is not null) consulta = consulta.Where(c => c.Estado == estado);
        return await consulta.OrderBy(c => c.RazonSocial).ToListAsync(ct);
    }

    public async Task<ComercioVista?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await Consulta.SingleOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Guid?> ObtenerRelacionIdAsync(Guid comercioId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.RelacionesComerciales.AsNoTracking().Where(r => r.ComercioId == comercioId)
            .Select(r => (Guid?)r.Id).SingleOrDefaultAsync(ct);
    }

    public async Task<Comercio?> BuscarDocumentoAsync(string documento, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Comercios.SingleOrDefaultAsync(c => c.DocumentoFiscal == documento, ct);
    }

    public async Task<RelacionComercial?> BloquearRelacionAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        // SQL parametrizado, más el filtro Tenant que EF compone; xmin sostiene la versión del formulario.
        return await db.RelacionesComerciales.FromSql(
            $"""SELECT *, xmin FROM administracion."RelacionesComerciales" WHERE "ComercioId" = {id} AND "OperadorId" = {tenant.OperadorId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
    }

    public void Agregar(Comercio comercio) => db.Comercios.Add(comercio);
    public void Agregar(RelacionComercial relacion) => db.RelacionesComerciales.Add(relacion);

    public async Task GuardarAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new DomainException("La relación cambió. Recargá la página antes de confirmar."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new DomainException("El comercio o la relación ya fue creado por otro administrador. Volvé a buscarlo."); }
    }
}
