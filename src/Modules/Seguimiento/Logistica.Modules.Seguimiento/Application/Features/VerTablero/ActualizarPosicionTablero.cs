using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Ejecucion.Contracts.Events;

namespace Logistica.Modules.Seguimiento.Application.Features.VerTablero;

// CU-53 → CU-66: cada posición nueva del vehículo se manda al tablero de su operador. El evento llega en memoria,
// después de que Ejecución guardó la posición; si falla el aviso, la posición ya quedó guardada y la foto inicial la
// vuelve a traer. SoloHistorial: la ruta ya estaba rendida, así que no se reactiva el vehículo en el mapa.
internal sealed class ActualizarPosicionTablero(ITableroEnVivo tablero) : IConsumidorPosicionReportada
{
    public Task ConsumirAsync(PosicionReportada evento, CancellationToken ct)
    {
        if (evento.SoloHistorial)
        {
            return Task.CompletedTask;
        }

        var p = evento.Posicion;
        return tablero.PosicionAsync(p.OperadorId,
            new PosicionTableroResponse(p.Id, p.VehiculoId, p.RutaId, p.Latitud, p.Longitud, p.CapturadaEn), ct);
    }
}
