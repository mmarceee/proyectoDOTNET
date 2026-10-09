namespace Logistica.Modules.Deposito.Contracts;

public interface IDepositoModuleApi
{
    // El módulo consumidor proporciona únicamente bultos que el usuario puede consultar.
    Task<IReadOnlyList<Guid>> BultosRecibidosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct);
}
