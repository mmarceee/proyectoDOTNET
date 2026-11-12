using Logistica.Modules.Deposito.Application;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence;

internal sealed class OperacionesDeposito(DepositoDbContext db, ICurrentTenant tenant) : IOperacionesDeposito
{
    public async Task<string?> ResultadoAsync(Guid operacionId, string pedido, CancellationToken ct)
    {
        if (operacionId == Guid.Empty) throw new DomainException("La confirmación requiere un identificador.");
        await db.ParticiparEnTransaccionAsync(ct);
        var previa = await db.Set<OperacionCustodia>().SingleOrDefaultAsync(o => o.OperacionId == operacionId, ct);
        if (previa is null) return null;
        if (previa.Huella != OperacionCustodia.CalcularHuella(pedido))
            throw new DomainException("Ese identificador de operación ya se usó con otro contenido o responsable.");
        return previa.Resultado;
    }
    public async Task RegistrarAsync(Guid operacionId, string pedido, string resultado, CancellationToken ct)
    {
        db.Add(OperacionCustodia.Registrar(tenant.OperadorId ?? throw new DomainException("Falta el operador."),
            operacionId, pedido, resultado));
        await db.SaveChangesAsync(ct);
    }
}
