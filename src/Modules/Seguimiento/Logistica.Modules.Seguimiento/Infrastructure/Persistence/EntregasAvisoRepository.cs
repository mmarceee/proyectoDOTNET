using Logistica.Modules.Seguimiento.Domain.Avisos;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class EntregasAvisoRepository(SeguimientoDbContext db) : IEntregasAvisoRepository
{
    public void Agregar(EntregaAviso entrega) => db.EntregasAviso.Add(entrega);

    public Task<bool> ExisteAsync(Guid eventoId, Guid suscripcionAvisoId, CancellationToken ct) =>
        db.EntregasAviso.AnyAsync(e => e.EventoId == eventoId && e.SuscripcionAvisoId == suscripcionAvisoId, ct);

    public Task<EntregaAviso?> ObtenerAsync(Guid id, CancellationToken ct) =>
        db.EntregasAviso.Include(e => e.Intentos).SingleOrDefaultAsync(e => e.Id == id, ct);

    public async Task GuardarAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}

internal sealed class EnviosSeguidosRepository(SeguimientoDbContext db) : IEnviosSeguidosRepository
{
    public void Agregar(EnvioSeguido envio) => db.EnviosSeguidos.Add(envio);

    public Task<EnvioSeguido?> ObtenerAsync(Guid envioId, CancellationToken ct) =>
        db.EnviosSeguidos.SingleOrDefaultAsync(e => e.Id == envioId, ct);
}
