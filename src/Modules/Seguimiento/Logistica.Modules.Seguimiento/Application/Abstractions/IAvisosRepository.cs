using Logistica.Http.Contracts.Repartidor;

namespace Logistica.Modules.Seguimiento.Application.Abstractions;

internal interface IAvisosRepository
{
    Task<IReadOnlyList<AvisoRutaResponse>> ConsultarAsync(Guid repartidorId, int pagina, CancellationToken ct);
}
