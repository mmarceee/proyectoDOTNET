using Logistica.Modules.Deposito.Contracts.Results;

namespace Logistica.Modules.Deposito.Contracts;

public interface IDepositoModuleApi
{
    // El módulo consumidor proporciona únicamente bultos que el usuario puede consultar.
    Task<IReadOnlyList<Guid>> BultosRecibidosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct);
    Task<IReadOnlyList<MedidasBultoRecibido>> ObtenerMedidasRecepcionAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct);
}
