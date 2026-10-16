using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class AvisosRepository(SeguimientoDbContext db) : IAvisosRepository
{
    public async Task<IReadOnlyList<AvisoRutaResponse>> ConsultarAsync(Guid repartidorId, int pagina, CancellationToken ct)
        => await db.Avisos.AsNoTracking().Where(a => a.RepartidorId == repartidorId)
            .OrderByDescending(a => a.DespachadaEn).ThenByDescending(a => a.Id).Skip((pagina - 1) * 20).Take(20)
            .Select(a => new AvisoRutaResponse(a.Id, a.RutaId, a.FechaRuta, a.DespachadaEn, a.Revision)).ToListAsync(ct);
}
