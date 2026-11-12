namespace Logistica.Modules.Deposito.Application;

internal interface IOperacionesDeposito
{
    Task<string?> ResultadoAsync(Guid operacionId, string pedido, CancellationToken ct);
    Task RegistrarAsync(Guid operacionId, string pedido, string resultado, CancellationToken ct);
}
