using Logistica.Modules.Envios.Application.Features.CrearEnvio;

namespace Logistica.Modules.Envios.Application.Features.ImportarEnvios;

// Cada envío del archivo en su propio ámbito: DbContext, conexión y transacción nuevos (CU-11, requerimiento especial 3).
// Si un envío falla, su transacción se revierte, pero sus cambios quedarían en el DbContext y el siguiente los volvería
// a guardar. Lo implementa Infrastructure, que es quien crea los ámbitos, como el procesador de operaciones de CU-52.
internal interface IAmbitoPorEnvio
{
    Task<T> EjecutarAsync<T>(Func<CrearEnvioHandler, Task<T>> trabajo, CancellationToken ct);
}
