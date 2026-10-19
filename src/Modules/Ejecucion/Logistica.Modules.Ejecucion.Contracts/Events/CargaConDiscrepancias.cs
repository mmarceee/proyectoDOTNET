namespace Logistica.Modules.Ejecucion.Contracts.Events;

public sealed record CargaConDiscrepancias(Guid OperacionId, Guid RutaId, Guid RepartidorId,
    IReadOnlyList<Guid> EnviosIncompletos, IReadOnlyList<string> CodigosSobrantes)
{
    public const string Tipo = "CargaConDiscrepancias.v1";
}
