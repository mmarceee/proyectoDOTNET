namespace Logistica.Modules.Planificacion.Application.Abstractions;

// Registro privado de idempotencia de los comandos de Planificación.
internal interface IOperacionesBultos
{
    Task<string?> ResultadoAsync(Guid operacionId, string pedido, CancellationToken ct);
    Task RegistrarAsync(Guid operacionId, string pedido, string resultado, CancellationToken ct);
}
