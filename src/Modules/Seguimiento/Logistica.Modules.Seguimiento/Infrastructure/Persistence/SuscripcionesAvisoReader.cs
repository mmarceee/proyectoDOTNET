using Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class SuscripcionesAvisoReader(SeguimientoDbContext db) : ISuscripcionesAvisoReader
{
    // Proyección explícita: ni siquiera se lee el secreto cifrado para las consultas del Portal.
    private static IQueryable<SuscripcionAvisoVista> Proyectar(IQueryable<SuscripcionAviso> consulta) => consulta
        .Select(s => new SuscripcionAvisoVista(s.Id, s.UrlDestino, s.TiposEvento.ToArray(), s.Activa));

    public async Task<IReadOnlyList<SuscripcionAvisoVista>> ListarAsync(CancellationToken ct) =>
        await Proyectar(db.SuscripcionesAviso.AsNoTracking().OrderBy(s => s.Id)).ToListAsync(ct);

    public Task<SuscripcionAvisoVista?> ObtenerAsync(Guid id, CancellationToken ct) =>
        Proyectar(db.SuscripcionesAviso.AsNoTracking().Where(s => s.Id == id)).SingleOrDefaultAsync(ct);
}
