using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Seguimiento.Application.Abstractions;

namespace Logistica.Modules.Seguimiento.Application.Features.ConsultarAvisos;

internal sealed class ConsultarAvisosHandler(IAvisosRepository avisos)
{
    public Task<IReadOnlyList<AvisoRutaResponse>> HandleAsync(Guid repartidorId, int pagina, CancellationToken ct)
        => avisos.ConsultarAsync(repartidorId, pagina, ct);
}
