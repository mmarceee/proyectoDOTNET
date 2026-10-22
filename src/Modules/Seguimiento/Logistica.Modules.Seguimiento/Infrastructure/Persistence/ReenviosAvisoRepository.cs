using Logistica.Modules.Seguimiento.Application.Features.HistorialAvisos;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class ReenviosAvisoRepository(SeguimientoDbContext db, ICurrentTenant tenant) : IReenviosAvisoRepository
{
    public async Task<EntregaAviso?> BloquearEntregaAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.EntregasAviso.FromSql($"""SELECT * FROM seguimiento.entregas_aviso WHERE "Id" = {id} AND "OperadorId" = {tenant.OperadorId} AND "ComercioId" = {tenant.ComercioId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
    }

    public async Task<SuscripcionAviso?> BloquearSuscripcionAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.SuscripcionesAviso.FromSql($"""SELECT * FROM seguimiento.suscripciones_aviso WHERE "Id" = {id} AND "OperadorId" = {tenant.OperadorId} AND "ComercioId" = {tenant.ComercioId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
    }

    public async Task GuardarAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
