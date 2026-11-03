using Logistica.Modules.Envios.Application.Features.CrearEnvio;
using Logistica.Modules.Envios.Application.Features.ImportarEnvios;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Envios.Infrastructure;

// CU-11: cada envío del archivo en un ámbito de DI nuevo, con su DbContext, su conexión y su unidad de trabajo. El
// inquilino sigue siendo el de la sesión, porque se lee del request (como ProcesadorEnAmbitoPropio, de CU-52).
internal sealed class AmbitoPorEnvio(IServiceScopeFactory ambitos) : IAmbitoPorEnvio
{
    public async Task<T> EjecutarAsync<T>(Func<CrearEnvioHandler, Task<T>> trabajo, CancellationToken ct)
    {
        await using var ambito = ambitos.CreateAsyncScope();
        return await trabajo(ambito.ServiceProvider.GetRequiredService<CrearEnvioHandler>());
    }
}
