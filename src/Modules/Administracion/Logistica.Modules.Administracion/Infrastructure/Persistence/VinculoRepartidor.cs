using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// El vínculo entre un usuario y su Repartidor (modelo de dominio, Repartidor.usuarioId). Al iniciar sesión todavía no
// hay inquilino, y el filtro "Tenant" escondería al repartidor: en lugar de ignorarlo (ADR-0002, sección 2.4), se
// consulta con el operador del usuario fijado como inquilino.
internal sealed class VinculoRepartidor(IServiceProvider servicios)
{
    // El repartidor activo vinculado al usuario, o null si no tiene.
    public async Task<Guid?> RepartidorDeAsync(Guid operadorId, Guid usuarioId, CancellationToken ct)
    {
        await using var db = ContextoDe(operadorId);

        return await db.Repartidores.AsNoTracking()
            .Where(r => r.UsuarioId == usuarioId && r.Activo)
            .Select(r => (Guid?)r.Id)
            .SingleOrDefaultAsync(ct);
    }

    // Lo usan los datos iniciales; la pantalla para vincular llega con CU-02 y CU-08. El interceptor valida la escritura
    // igual que en un request (sección 2.8).
    public async Task VincularAsync(Guid operadorId, Guid repartidorId, Guid usuarioId, CancellationToken ct)
    {
        await using var db = ContextoDe(operadorId);

        var repartidor = await db.Repartidores.SingleAsync(r => r.Id == repartidorId, ct);
        repartidor.VincularUsuario(usuarioId);
        await db.SaveChangesAsync(ct);
    }

    private AdministracionDbContext ContextoDe(Guid operadorId)
    {
        return ActivatorUtilities.CreateInstance<AdministracionDbContext>(servicios, new InquilinoFijo(operadorId, null));
    }
}
