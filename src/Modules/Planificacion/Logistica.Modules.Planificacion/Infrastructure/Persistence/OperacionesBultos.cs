using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence;

internal sealed class OperacionesBultos(PlanificacionDbContext db, ICurrentTenant tenant) : IOperacionesBultos
{
    public async Task<string?> ResultadoAsync(Guid operacionId, string pedido, CancellationToken ct)
    {
        if (operacionId == Guid.Empty) throw new DomainException("La operación requiere un identificador.");
        await db.ParticiparEnTransaccionAsync(ct);
        var previa = await db.Set<OperacionBultos>().SingleOrDefaultAsync(o => o.OperacionId == operacionId, ct);
        if (previa is null) return null;
        if (previa.Huella != OperacionBultos.CalcularHuella(pedido))
            throw new DomainException("Ese identificador de operación ya se usó con otro contenido o responsable.");
        return previa.Resultado;
    }
    public async Task RegistrarAsync(Guid operacionId, string pedido, string resultado, CancellationToken ct)
    {
        db.Add(OperacionBultos.Registrar(tenant.OperadorId ?? throw new DomainException("Falta el operador."),
            operacionId, pedido, resultado));
        await db.SaveChangesAsync(ct);
    }
}
