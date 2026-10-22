using Logistica.Modules.Ejecucion.Contracts.Results;
using Logistica.Modules.Ejecucion.Domain.Posiciones;

namespace Logistica.Modules.Ejecucion.Application.Mapping;

internal static class PosicionMapper
{
    public static PosicionVehiculoResult Mapear(PosicionVehiculo p) => new(p.Id, p.OperadorId, p.VehiculoId,
        p.RutaId, p.Latitud, p.Longitud, p.CapturadaEn, p.RecibidaEn);
}
