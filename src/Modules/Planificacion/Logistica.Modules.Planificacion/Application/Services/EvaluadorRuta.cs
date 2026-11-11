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
    IEnviosModuleApi envios, PreparadorEnviosRuta preparador, ICurrentTenant tenant, TimeProvider reloj,
    IReservaBultoRepository reservas)
{
    private Guid Operador => tenant.OperadorId ?? throw new DomainException("La sesión no tiene un operador.");
    public async Task<DatosEvaluacion> CargarAsync(DateOnly fecha, Guid repartidorId, Guid vehiculoId,
        IReadOnlyList<Guid> todos, IReadOnlyList<Guid> nuevos, Guid? propia, CancellationToken ct,
        IReadOnlyList<Guid>? seleccionBultos = null, IReadOnlyDictionary<Guid, long>? revisiones = null)
    {
        var recursos = await administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
        var repartidor = recursos.Repartidores.SingleOrDefault(r => r.Id == repartidorId)
            ?? throw new DomainException("El repartidor no existe o está inactivo.");
        var vehiculoDto = recursos.Vehiculos.SingleOrDefault(v => v.Id == vehiculoId) ?? throw new DomainException("El vehículo no existe o está inactivo.");
        var ocupados = todos.Count == 0 ? [] : await rutas.RecursosOcupadosAsync(fecha, repartidorId, vehiculoId, propia, ct);
        if (ocupados.Count > 0) throw new ConflictoRutaException(ocupados[0], "Un recurso dejó de estar disponible. Elegí otro y confirmá nuevamente.", recursos: ocupados);
        var leidos = await envios.ObtenerParaPlanificacionAsync(todos, ct);
        var noDisponibles = todos.Where(id => !leidos.Any(e => e.Id == id && e.OperadorId == Operador)).ToList();
        var ocupadosBultos = (await reservas.OcupadosAsync(leidos.SelectMany(e => e.Bultos).Select(b => b.Id).ToArray(), propia, ct)).ToHashSet();
        var manifiesto = propia is Guid rutaId ? (await reservas.DeRutaAsync(rutaId, ct)).Where(r => r.CerradaEn == null).ToArray() : [];
        if (seleccionBultos is not null && (seleccionBultos.Count == 0
            || seleccionBultos.Distinct().Count() != seleccionBultos.Count
            || seleccionBultos.Any(id => !leidos.Any(e => nuevos.Contains(e.Id) && e.Bultos.Any(b => b.Id == id)))))
            throw new DomainException("Seleccioná bultos válidos de los envíos nuevos, sin duplicados.");
        var seleccionadosNuevos = new List<Guid>();
        leidos = leidos.Select(e =>
        {
            var existentes = e.Bultos.Where(b => manifiesto.Any(r => r.EnvioId == e.Id && r.BultoId == b.Id)
                && b.Estado == "AsignadoARuta" && b.Custodia == "Deposito").ToArray();
            if (existentes.Length != manifiesto.Count(r => r.EnvioId == e.Id)) noDisponibles.Add(e.Id);
            var agregados = nuevos.Contains(e.Id) ? e.Bultos.Where(b => (seleccionBultos is null || seleccionBultos.Contains(b.Id))
                && b.Estado is "EnDeposito" or "Reprogramado" && b.Custodia == "Deposito"
                && !ocupadosBultos.Contains(b.Id)).ToArray() : [];
            if (nuevos.Contains(e.Id) && agregados.Length == 0) noDisponibles.Add(e.Id);
            seleccionadosNuevos.AddRange(agregados.Select(b => b.Id));
            return e with { Bultos = existentes.Concat(agregados).ToArray() };
        }).ToArray();
        if (seleccionBultos is not null && seleccionadosNuevos.Count != seleccionBultos.Count)
            throw new ConflictoRutaException("BultoNoDisponible", "Algún bulto dejó de estar disponible. Recargá la selección.", nuevos);
        if (seleccionBultos is not null && (revisiones is null || seleccionBultos.Any(id => !revisiones.ContainsKey(id))))
            throw new DomainException("La selección por bulto requiere su revisión de custodia.");
        if (seleccionBultos is not null && leidos.SelectMany(e => e.Bultos).Any(b => seleccionBultos.Contains(b.Id)
            && revisiones![b.Id] != b.RevisionCustodia))
            throw new ConflictoRutaException("CustodiaModificada", "La custodia cambió. Recargá antes de reservar.", nuevos);
        noDisponibles.AddRange(leidos.Where(e => e.Bultos.Count == 0).Select(e => e.Id));
        if (noDisponibles.Count > 0) throw new ConflictoRutaException("EnvioNoDisponible", "Algunos envíos ya no están disponibles. Revisá y confirmá el resto.", noDisponibles.Distinct().ToList());
        leidos = leidos.Select(e => e with { ReintentoDesde = e.Bultos.Max(b => b.ReintentoDesde) }).ToArray();
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
