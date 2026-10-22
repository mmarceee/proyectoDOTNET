using Logistica.Modules.Ejecucion.Contracts.Results;

namespace Logistica.Modules.Ejecucion.Contracts.Events;

// Evento efímero, posterior al commit. CU-66 registra sus consumidores en DI; no usa RabbitMQ ni Outbox.
public sealed record PosicionReportada(PosicionVehiculoResult Posicion, bool SoloHistorial);

public interface IConsumidorPosicionReportada
{
    Task ConsumirAsync(PosicionReportada evento, CancellationToken ct);
}
