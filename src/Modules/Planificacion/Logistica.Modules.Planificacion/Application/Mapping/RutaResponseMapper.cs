using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Domain.Rutas;

namespace Logistica.Modules.Planificacion.Application.Mapping;

internal static class RutaResponseMapper
{
    public static RutaResponse Mapear(Ruta ruta, EvaluacionRutaResponse? carga = null)
        => new(ruta.Id, ruta.Fecha, ruta.RepartidorId, ruta.VehiculoId, ruta.Estado.ToString(), ruta.Revision, ruta.ReservaActiva,
            ruta.Paradas.OrderBy(p => p.Orden).Select(p => new ParadaRutaResponse(p.Id, p.EnvioId, p.Orden, p.Estado.ToString())).ToList(), carga);
}
