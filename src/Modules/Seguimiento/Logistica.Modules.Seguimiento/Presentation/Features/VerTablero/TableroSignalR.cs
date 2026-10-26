using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Seguimiento.Application.Features.VerTablero;
using Microsoft.AspNetCore.SignalR;

namespace Logistica.Modules.Seguimiento.Presentation.Features.VerTablero;

// ITableroEnVivo con SignalR: manda cada novedad sólo a la sala del operador. Con el backplane de Valkey, también
// funciona cuando avisa el Worker, que no tiene navegadores conectados (decisión G1).
internal sealed class TableroSignalR(IHubContext<TableroHub> hub) : ITableroEnVivo
{
    public Task PosicionAsync(Guid operadorId, PosicionTableroResponse posicion, CancellationToken ct)
    {
        return hub.Clients.Group(TableroHub.Sala(operadorId)).SendAsync("posicion", posicion, ct);
    }

    public Task NovedadAsync(Guid operadorId, NovedadTableroResponse novedad, CancellationToken ct)
    {
        return hub.Clients.Group(TableroHub.Sala(operadorId)).SendAsync("novedad", novedad, ct);
    }
}
