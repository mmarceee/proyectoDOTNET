namespace Logistica.Modules.Ejecucion.Contracts.Events;

// ADR-0004 §2.5: aviso durable para CU-66; la evidencia permanece en operaciones_dispositivo.
public sealed record ConflictoSincronizacionRegistrado(Guid OperacionId, Guid RutaId, Guid? EnvioId,
    Guid RepartidorId, string Resultado, string? Motivo)
{
    public const string Tipo = "ConflictoSincronizacionRegistrado.v1";
}
