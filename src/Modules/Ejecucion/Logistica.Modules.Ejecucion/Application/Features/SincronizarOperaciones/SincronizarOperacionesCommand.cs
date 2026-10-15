using Logistica.Http.Contracts.Repartidor;

namespace Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;

// El lote que mandó la PWA. RepartidorId y UsuarioId salen de la sesión (claims), nunca del cuerpo del pedido.
internal sealed record SincronizarOperacionesCommand(Guid RepartidorId, Guid? UsuarioId,
    IReadOnlyList<OperacionDispositivoRequest> Operaciones);

// Una operación del lote, con lo que el lote sabe de ella.
internal sealed record ProcesarOperacionCommand(Guid RepartidorId, Guid? UsuarioId, OperacionDispositivoRequest Operacion,
    bool EnvioConRechazoPrevio);
