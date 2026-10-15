using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Ejecucion.Infrastructure;

// Cada operación del lote en un ámbito de DI nuevo: DbContext, conexión y unidad de trabajo propios (ADR-0004,
// sección 2.6). El inquilino sigue siendo el de la sesión, porque se lee del request. Si otra operación cambió el
// mismo envío en ese instante (concurrencia optimista), se reevalúa una vez con un ámbito limpio.
internal sealed class ProcesadorEnAmbitoPropio(IServiceScopeFactory ambitos) : IProcesadorDeOperacion
{
    public async Task<ResultadoOperacionResponse> ProcesarAsync(ProcesarOperacionCommand command, CancellationToken ct)
    {
        try
        {
            return await ProcesarEnAmbitoNuevoAsync(command, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ProcesarEnAmbitoNuevoAsync(command, ct);
        }
    }

    private async Task<ResultadoOperacionResponse> ProcesarEnAmbitoNuevoAsync(ProcesarOperacionCommand command, CancellationToken ct)
    {
        await using var ambito = ambitos.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ProcesarOperacionHandler>().HandleAsync(command, ct);
    }
}
