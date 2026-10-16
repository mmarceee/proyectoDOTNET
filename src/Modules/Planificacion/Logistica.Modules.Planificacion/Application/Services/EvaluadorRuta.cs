using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Services;

// Evaluación compartida por CU-40 y CU-43; no modifica la ruta ni congela las reglas.
internal sealed class EvaluadorRuta(IRutaRepository rutas, IAdministracionModuleApi administracion,
    IEnviosModuleApi envios, PreparadorEnviosRuta preparador, ICurrentTenant tenant, TimeProvider reloj)
{
    private Guid Operador => tenant.OperadorId ?? throw new DomainException("La sesión no tiene un operador.");
    public async Task<DatosEvaluacion> CargarAsync(DateOnly fecha, Guid repartidorId, Guid vehiculoId,
        IReadOnlyList<Guid> todos, IReadOnlyList<Guid> nuevos, Guid? propia, CancellationToken ct)
    {
        var recursos = await administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
        var repartidor = recursos.Repartidores.SingleOrDefault(r => r.Id == repartidorId)
            ?? throw new DomainException("El repartidor no existe o está inactivo.");
        var vehiculoDto = recursos.Vehiculos.SingleOrDefault(v => v.Id == vehiculoId) ?? throw new DomainException("El vehículo no existe o está inactivo.");
        var ocupados = todos.Count == 0 ? [] : await rutas.RecursosOcupadosAsync(fecha, repartidorId, vehiculoId, propia, ct);
        if (ocupados.Count > 0) throw new ConflictoRutaException(ocupados[0], "Un recurso dejó de estar disponible. Elegí otro y confirmá nuevamente.", recursos: ocupados);
        var leidos = await envios.ObtenerParaPlanificacionAsync(todos, ct);
        var noDisponibles = todos.Where(id => !leidos.Any(e => e.Id == id && e.OperadorId == Operador)).ToList();
        noDisponibles.AddRange(leidos.Where(e => nuevos.Contains(e.Id) && e.Estado is not ("EnDeposito" or "Reprogramado")).Select(e => e.Id));
        noDisponibles.AddRange(await rutas.EnviosOcupadosAsync(todos, propia, ct));
        noDisponibles.AddRange(leidos.Where(e => !nuevos.Contains(e.Id) && e.Estado != "AsignadoARuta").Select(e => e.Id));
        if (noDisponibles.Count > 0) throw new ConflictoRutaException("EnvioNoDisponible", "Algunos envíos ya no están disponibles. Revisá y confirmá el resto.", noDisponibles.Distinct().ToList());
        var reglas = await administracion.ObtenerReglasPlanificacionAsync(reloj.GetUtcNow(), ct);
        var preparados = await preparador.PrepararAsync(todos.Select(id => leidos.Single(e => e.Id == id)).ToList(), fecha, recursos, ct);
        var vehiculo = new VehiculoRuta(vehiculoDto.Id, vehiculoDto.CapacidadPesoKg, vehiculoDto.CapacidadVolumenM3,
            vehiculoDto.LargoCargaCm, vehiculoDto.AnchoCargaCm, vehiculoDto.AltoCargaCm);
        return new(vehiculo, reglas, preparados, PlanificadorDeRuta.Evaluar(fecha, vehiculo, reglas.MaxParadasPorRuta, preparados), repartidor);
    }
    public static void ExigirValida(EvaluacionRuta evaluacion)
    { if (!evaluacion.Valida) throw new RestriccionesRutaException(evaluacion.Restricciones); }
    public static EvaluacionRutaResponse Resumen(EvaluacionRuta e, VehiculoRuta v, ReglasPlanificacion reglas)
        => new(e.Valida, e.PesoKg, e.VolumenM3, e.Paradas, v.PesoKg, v.VolumenM3, reglas.MaxParadasPorRuta,
            e.Restricciones.Select(r => new RestriccionRutaResponse(r.Codigo, r.Mensaje, r.EnvioId, r.BultoId)).ToList());
    internal sealed record DatosEvaluacion(VehiculoRuta Vehiculo, ReglasPlanificacion Reglas, IReadOnlyList<EnvioRuta> Envios,
        EvaluacionRuta Evaluacion, RepartidorPlanificacion Repartidor);
}
