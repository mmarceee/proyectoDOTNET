namespace Logistica.Modules.Envios.Contracts.Results;

public sealed record EnvioParaCarga(Guid Id, string Estado, IReadOnlyList<BultoParaCarga> Bultos)
{
    public string Numero { get; init; } = "";
}
public sealed record BultoParaCarga(Guid Id, string Codigo);
