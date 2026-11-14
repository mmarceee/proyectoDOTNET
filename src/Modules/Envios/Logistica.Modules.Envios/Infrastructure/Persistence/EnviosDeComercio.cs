using Logistica.Modules.Envios.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnviosDeComercio(EnviosDbContext db) : IEnviosDeComercio
{
    // CU-01, baja de la relación: los envíos del comercio que todavía tienen trabajo pendiente. Se decide por Terminado
    // (todos los bultos en un estado terminal; cierre técnico RF 31, D4) y no por el estado de compatibilidad del envío:
    // un parcial abierto sigue pendiente aunque ese estado diga Entregado, y un cierre mixto (unos bultos entregados y
    // otros devueltos) ya no lo está.
    public async Task<int> ContarPendientesAsync(Guid comercioId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Envios.CountAsync(e => e.ComercioId == comercioId && !e.Terminado, ct);
    }
}
