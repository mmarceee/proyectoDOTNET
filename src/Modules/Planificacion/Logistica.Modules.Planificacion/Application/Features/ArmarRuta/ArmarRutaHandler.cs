using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Application.Services;
using Logistica.SharedKernel;
using static Logistica.Modules.Planificacion.Application.Mapping.RutaResponseMapper;

namespace Logistica.Modules.Planificacion.Application.Features.ArmarRuta;

internal sealed class ArmarRutaHandler(IRutaRepository rutas, IAdministracionModuleApi administracion,
    IEnviosModuleApi envios, PreparadorEnviosRuta preparador, IConfirmacionPlanificacion confirmacion,
    ICurrentTenant tenant, IResponsablePlanificacion responsable, TimeProvider reloj, CalendarioPlanificacion calendario)
{
    private Guid Operador => tenant.OperadorId ?? throw new DomainException("La sesión no tiene un operador.");
    public async Task<EvaluacionRutaResponse> PrevalidarAsync(PrevalidarRutaCommand pedido, CancellationToken ct)
    {
        ValidarSeleccion(pedido.EnvioIds, permitirVacia: pedido.RutaId is not null);
        var ruta = pedido.RutaId is Guid id ? await ExigirRutaAsync(id, ct) : null;
        ExigirEditable(ruta, pedido.RevisionEsperada);
        if (ruta is null || pedido.Fecha != ruta.Fecha)
            PlanificadorDeRuta.ExigirFechaVigente(pedido.Fecha, await calendario.HoyAsync(ct));
        var todos = (ruta?.Paradas.Select(p => p.EnvioId) ?? []).Concat(pedido.EnvioIds).ToArray();
        if (todos.Distinct().Count() != todos.Length) throw new DomainException("La selección incluye un envío ya presente o repetido.");
        var datos = await CargarEvaluacionAsync(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId, todos,
            pedido.EnvioIds, ruta?.Id, ct);
        return Resumen(datos.Evaluacion, datos.Vehiculo, datos.Reglas);
    }
    public async Task<RutaResponse> CrearAsync(CrearRutaCommand pedido, CancellationToken ct)
    {
        ValidarSeleccion(pedido.EnvioIds, false);
        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            await administracion.BloquearPlanificacionAsync(ct);
            PlanificadorDeRuta.ExigirFechaVigente(pedido.Fecha, await calendario.HoyAsync(ct));
            var datos = await CargarEvaluacionAsync(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId, pedido.EnvioIds,
                pedido.EnvioIds, null, ct);
            ExigirValida(datos.Evaluacion);
            var ruta = Ruta.Crear(Operador, pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId, pedido.EnvioIds, reloj.GetUtcNow());
            rutas.Agregar(ruta);
            await AsignarAsync(ruta, pedido.EnvioIds, datos.Envios, ct);
            await GuardarValidacionAsync(ruta, OperacionValidacionRuta.Creacion, datos, ct);
            respuesta = Mapear(ruta, Resumen(datos.Evaluacion, datos.Vehiculo, datos.Reglas));
        }, pedido.EnvioIds, ct);
        return respuesta!;
    }
    public async Task<RutaResponse> AgregarAsync(Guid id, AgregarEnviosRutaCommand pedido, CancellationToken ct)
    {
        ValidarSeleccion(pedido.EnvioIds, false);
        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            await administracion.BloquearPlanificacionAsync(ct);
            var ruta = await ExigirRutaAsync(id, ct); ExigirEditable(ruta, pedido.RevisionEsperada);
            var todos = ruta.Paradas.Select(p => p.EnvioId).Concat(pedido.EnvioIds).ToArray();
            if (todos.Distinct().Count() != todos.Length) throw new DomainException("Algún envío ya pertenece a la ruta.");
            var datos = await CargarEvaluacionAsync(ruta.Fecha, ruta.RepartidorId, ruta.VehiculoId, todos, pedido.EnvioIds, ruta.Id, ct);
            ExigirValida(datos.Evaluacion);
            ruta.AgregarEnvios(pedido.EnvioIds);
            await AsignarAsync(ruta, pedido.EnvioIds, datos.Envios, ct);
            await GuardarValidacionAsync(ruta, OperacionValidacionRuta.Agregado, datos, ct);
            respuesta = Mapear(ruta, Resumen(datos.Evaluacion, datos.Vehiculo, datos.Reglas));
        }, pedido.EnvioIds, ct);
        return respuesta!;
    }
    public async Task<RutaResponse> ModificarAsync(Guid id, ModificarRutaCommand pedido, CancellationToken ct)
    {
        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            await administracion.BloquearPlanificacionAsync(ct);
            var ruta = await ExigirRutaAsync(id, ct); ExigirEditable(ruta, pedido.RevisionEsperada);
            if (pedido.Fecha != ruta.Fecha)
                PlanificadorDeRuta.ExigirFechaVigente(pedido.Fecha, await calendario.HoyAsync(ct));
            var datos = await CargarEvaluacionAsync(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId,
                ruta.Paradas.Select(p => p.EnvioId).ToArray(), [], ruta.Id, ct);
            ExigirValida(datos.Evaluacion);
            var anterior = ruta.Revision;
            ruta.ModificarPlanificacion(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId);
            if (ruta.Revision != anterior) await GuardarValidacionAsync(ruta, OperacionValidacionRuta.Modificacion, datos, ct);
            respuesta = Mapear(ruta, Resumen(datos.Evaluacion, datos.Vehiculo, datos.Reglas));
        }, [], ct);
        return respuesta!;
    }
    private async Task<DatosEvaluacion> CargarEvaluacionAsync(DateOnly fecha, Guid repartidorId, Guid vehiculoId,
        IReadOnlyList<Guid> todos, IReadOnlyList<Guid> nuevos, Guid? propia, CancellationToken ct)
    {
        var recursos = await administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
        if (!recursos.Repartidores.Any(r => r.Id == repartidorId)) throw new DomainException("El repartidor no existe o está inactivo.");
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
        var preparados = await preparador.PrepararAsync(leidos, fecha, recursos, ct);
        var vehiculo = new VehiculoRuta(vehiculoDto.Id, vehiculoDto.CapacidadPesoKg, vehiculoDto.CapacidadVolumenM3,
            vehiculoDto.LargoCargaCm, vehiculoDto.AnchoCargaCm, vehiculoDto.AltoCargaCm);
        return new(vehiculo, reglas, preparados, PlanificadorDeRuta.Evaluar(fecha, vehiculo, reglas.MaxParadasPorRuta, preparados));
    }
    private Task AsignarAsync(Ruta ruta, IReadOnlyList<Guid> nuevos, IReadOnlyList<EnvioRuta> preparados, CancellationToken ct)
        => envios.AsignarARutaAsync(ruta.Id, nuevos.Select(id => new AsignacionEnvioRuta(id, preparados.Single(e => e.Id == id).Franja?.Id)).ToList(), responsable.Id, ct);
    private async Task GuardarValidacionAsync(Ruta ruta, OperacionValidacionRuta operacion, DatosEvaluacion datos, CancellationToken ct)
    {
        rutas.Agregar(ValidacionRuta.Registrar(ruta, operacion, datos.Vehiculo, datos.Reglas.VersionId,
            datos.Reglas.MaxParadasPorRuta, datos.Envios, responsable.Id, reloj.GetUtcNow()));
        await rutas.GuardarAsync(ct);
    }
    private async Task<Ruta> ExigirRutaAsync(Guid id, CancellationToken ct)
        => await rutas.ObtenerAsync(id, ct) ?? throw new RutaNoEncontradaException();
    private static void ExigirEditable(Ruta? ruta, long? esperada)
    {
        if (ruta is null) return;
        if (ruta.Estado != EstadoRuta.Planificada) throw new ConflictoRutaException("RutaNoEditable", "La ruta ya no está Planificada.");
        if (esperada is long revision && ruta.Revision != revision)
            throw new ConflictoRutaException("RutaModificada", "Otra operación modificó la ruta. Recargá y confirmá nuevamente.", revision: ruta.Revision);
    }
    private static void ValidarSeleccion(IReadOnlyList<Guid>? ids, bool permitirVacia)
    {
        if (ids is null || (!permitirVacia && ids.Count == 0) || ids.Count > 1000 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            throw new DomainException("Seleccioná envíos válidos, sin duplicados (máximo 1000 por solicitud).");
    }
    private static void ExigirValida(EvaluacionRuta evaluacion)
    { if (!evaluacion.Valida) throw new RestriccionesRutaException(evaluacion.Restricciones); }
    private static EvaluacionRutaResponse Resumen(EvaluacionRuta e, VehiculoRuta v, ReglasPlanificacion reglas)
        => new(e.Valida, e.PesoKg, e.VolumenM3, e.Paradas, v.PesoKg, v.VolumenM3, reglas.MaxParadasPorRuta,
            e.Restricciones.Select(r => new RestriccionRutaResponse(r.Codigo, r.Mensaje, r.EnvioId, r.BultoId)).ToList());
    private sealed record DatosEvaluacion(VehiculoRuta Vehiculo, ReglasPlanificacion Reglas, IReadOnlyList<EnvioRuta> Envios, EvaluacionRuta Evaluacion);
}
