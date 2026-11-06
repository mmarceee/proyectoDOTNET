using Logistica.Modules.Deposito.Contracts.Results;

namespace Logistica.Modules.Deposito.Contracts;

public interface IDepositoModuleApi
{
    // El módulo consumidor proporciona únicamente bultos que el usuario puede consultar.
    Task<IReadOnlyList<Guid>> BultosRecibidosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct);
    Task<IReadOnlyList<MedidasBultoRecibido>> ObtenerMedidasRecepcionAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct);

    // CU-72: por comercio, las liquidaciones emitidas o pagadas del operador de la sesión cuyo período se superpone con
    // el pedido (días del operador, ambos incluidos).
    Task<IReadOnlyList<ResumenLiquidacionesComercio>> ResumirLiquidacionesAsync(DateOnly desde, DateOnly hasta, CancellationToken ct);
}
