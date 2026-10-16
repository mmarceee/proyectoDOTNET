using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnviosDeComercio(EnviosDbContext db) : IEnviosDeComercio
{
    public async Task<int> ContarPendientesAsync(Guid comercioId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Envios.CountAsync(e => e.ComercioId == comercioId
            && e.Estado != EstadoEnvio.Entregado && e.Estado != EstadoEnvio.Devuelto
            && e.Estado != EstadoEnvio.Cancelado && e.Estado != EstadoEnvio.Extraviado, ct);
    }
}
