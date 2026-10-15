using Logistica.Http.Contracts.Repartidor;

namespace Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;

// Procesa una operación en su propio ámbito: DbContext y transacción nuevos (ADR-0004, sección 2.6). Si una operación
// falla, su transacción se revierte, pero sus cambios quedarían en los DbContext y la siguiente los volvería a guardar.
// Lo implementa Infrastructure, que es quien crea los ámbitos.
internal interface IProcesadorDeOperacion
{
    Task<ResultadoOperacionResponse> ProcesarAsync(ProcesarOperacionCommand command, CancellationToken ct);
}
