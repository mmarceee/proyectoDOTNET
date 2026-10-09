using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Rutas;

internal sealed class PlanificacionService(IRutaRepository rutas, IAdministracionModuleApi administracion,
    IEnviosModuleApi envios, IDepositoModuleApi deposito, IConfirmacionPlanificacion confirmacion,
    ICurrentTenant tenant, IResponsablePlanificacion responsable, TimeProvider reloj)
{
    private Guid Operador => tenant.OperadorId ?? throw new DomainException("La sesión no tiene un operador.");
    public Task<RecursosPlanificacion> RecursosAsync(DateOnly fecha, CancellationToken ct)
        => administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
    public async Task<DateOnly> HoyAsync(CancellationToken ct)
    {
        var contexto = await administracion.ObtenerContextoPlanificacionAsync(ct);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(contexto.ZonaHoraria)).DateTime);
    }
    public async Task<IReadOnlyDictionary<Guid, string>> NumerosEnvioAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => (await envios.ObtenerParaPlanificacionAsync(ids, ct)).ToDictionary(e => e.Id, e => e.Numero);
    public async Task<IReadOnlyList<RutaResponse>> ListarAsync(DateOnly? fecha, CancellationToken ct)
        => (await rutas.ListarAsync(fecha, ct)).Select(r => Mapear(r)).ToList();
    public async Task<RutaResponse> DetalleAsync(Guid id, CancellationToken ct) => Mapear(await ExigirRutaAsync(id, ct));
    public async Task<IReadOnlyList<ValidacionRutaResponse>> ValidacionesAsync(Guid id, int pagina, CancellationToken ct)
    {
        await ExigirRutaAsync(id, ct);
        if (pagina < 1) throw new DomainException("La página debe ser positiva.");
        return (await rutas.ValidacionesAsync(id, pagina, ct)).Select(v => new ValidacionRutaResponse(v.Id, v.RevisionRuta,
            v.ValidadaEn, v.ResponsableId, v.Operacion.ToString(), v.FechaRuta, v.RepartidorId, v.VehiculoId, v.VersionReglasId,
            v.MaxParadasPorRuta, v.CapacidadPesoKg, v.CapacidadVolumenM3, v.LargoCargaCm, v.AnchoCargaCm, v.AltoCargaCm,
            v.Bultos.Select(b => new BultoValidadoResponse(b.EnvioId, b.BultoId, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm, b.VolumenM3)).ToList())).ToList();
    }

    public async Task<EnviosDisponiblesResponse> DisponiblesAsync(DateOnly fecha, Guid? zona, Guid? franja,
        string? texto, int pagina, int tamano, CancellationToken ct, IReadOnlyCollection<Guid>? seleccion = null)
    {
        if (pagina < 1 || tamano is < 1 or > 100) throw new DomainException("La paginación debe estar entre 1 y 100 elementos.");
        var candidatos = await envios.ObtenerParaPlanificacionAsync(null, ct);
        var ocupados = (await rutas.EnviosOcupadosAsync(candidatos.Select(e => e.Id).ToArray(), null, ct)).ToHashSet();
        var recursos = await RecursosAsync(fecha, ct);
        var preparados = await PrepararAsync(candidatos, fecha, recursos, ct);
        var lista = candidatos.Where(e => !ocupados.Contains(e.Id) && (e.FechaEntregaProgramada is null || e.FechaEntregaProgramada == fecha))
            .Where(e => seleccion is null || seleccion.Contains(e.Id))
            .Select(e => (Envio: e, Datos: preparados.Single(p => p.Id == e.Id)))
            .Where(p => !p.Datos.FranjaRequerida || p.Datos.Franja is not null)
            .Where(p => zona is null || p.Datos.ZonaId == zona)
            .Where(p => franja is null || p.Datos.Franja?.Id == franja)
            .Where(p => string.IsNullOrWhiteSpace(texto) || p.Envio.Numero.Contains(texto, StringComparison.OrdinalIgnoreCase)
                || p.Envio.Direccion.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Envio.Numero).ToList();
        return new(lista.Skip((pagina - 1) * tamano).Take(tamano).Select(p => new EnvioDisponibleResponse(p.Envio.Id,
            p.Envio.Numero, p.Envio.Direccion, p.Datos.ZonaId, p.Datos.Franja?.Id, p.Envio.FechaEntregaProgramada,
            p.Datos.Bultos.Sum(b => b.PesoKg), p.Datos.Bultos.Sum(b => b.VolumenM3))).ToList(), lista.Count, pagina, tamano);
    }

    public async Task<EvaluacionRutaResponse> PrevalidarAsync(PrevalidarRutaRequest pedido, CancellationToken ct)
    {
        ValidarSeleccion(pedido.EnvioIds, permitirVacia: pedido.RutaId is not null);
        var ruta = pedido.RutaId is Guid id ? await ExigirRutaAsync(id, ct) : null;
        ExigirEditable(ruta, pedido.RevisionEsperada);
        var todos = (ruta?.Paradas.Select(p => p.EnvioId) ?? []).Concat(pedido.EnvioIds).ToArray();
        if (todos.Distinct().Count() != todos.Length) throw new DomainException("La selección incluye un envío ya presente o repetido.");
        var datos = await CargarEvaluacionAsync(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId, todos,
            pedido.EnvioIds, ruta?.Id, ct);
        return Resumen(datos.Evaluacion, datos.Vehiculo, datos.Reglas);
    }

    public async Task<RutaResponse> CrearAsync(CrearRutaRequest pedido, CancellationToken ct)
    {
        ValidarSeleccion(pedido.EnvioIds, false);
        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            await administracion.BloquearPlanificacionAsync(ct);
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

    public async Task<RutaResponse> AgregarAsync(Guid id, AgregarEnviosRutaRequest pedido, CancellationToken ct)
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

    public async Task<RutaResponse> ModificarAsync(Guid id, ModificarRutaRequest pedido, CancellationToken ct)
    {
        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            await administracion.BloquearPlanificacionAsync(ct);
            var ruta = await ExigirRutaAsync(id, ct); ExigirEditable(ruta, pedido.RevisionEsperada);
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
        var recursos = await RecursosAsync(fecha, ct);
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
        var preparados = await PrepararAsync(leidos, fecha, recursos, ct);
        var vehiculo = new VehiculoRuta(vehiculoDto.Id, vehiculoDto.CapacidadPesoKg, vehiculoDto.CapacidadVolumenM3,
            vehiculoDto.LargoCargaCm, vehiculoDto.AnchoCargaCm, vehiculoDto.AltoCargaCm);
        return new(vehiculo, reglas, preparados, PlanificadorDeRuta.Evaluar(fecha, vehiculo, reglas.MaxParadasPorRuta, preparados));
    }
    private async Task<IReadOnlyList<EnvioRuta>> PrepararAsync(IReadOnlyList<EnvioPlanificable> lista, DateOnly fecha,
        RecursosPlanificacion recursos, CancellationToken ct)
    {
        var medidas = (await deposito.ObtenerMedidasRecepcionAsync(lista.SelectMany(e => e.Bultos).Select(b => b.Id).ToArray(), ct)).ToDictionary(m => m.BultoId);
        var franjas = await administracion.ResolverFranjasAsync(lista.Where(e => e.FranjaHorariaId != null).Select(e => e.FranjaHorariaId!.Value).ToArray(), fecha, ct);
        return lista.Select(e =>
        {
            var zona = e.ZonaId ?? recursos.Zonas.SingleOrDefault(z => z.CodigosPostales.Contains(e.CodigoPostal))?.Id;
            var f = franjas.SingleOrDefault(f => f.ReferenciaId == e.FranjaHorariaId);
            var carga = e.Bultos.Select(b =>
            {
                medidas.TryGetValue(b.Id, out var m);
                return new CargaBulto(e.Id, b.Id, m?.PesoKg ?? b.PesoKg, m?.LargoCm ?? b.LargoCm,
                    m?.AnchoCm ?? b.AnchoCm, m?.AltoCm ?? b.AltoCm);
            }).ToList();
            return new EnvioRuta(e.Id, e.Numero, e.FechaEntregaProgramada, zona,
                f is null ? null : new FranjaRuta(f.Id, f.ZonaId, f.Desde, f.Hasta, f.Dias), e.FranjaHorariaId is not null, carga);
        }).ToList();
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
    private static RutaResponse Mapear(Ruta ruta, EvaluacionRutaResponse? carga = null)
        => new(ruta.Id, ruta.Fecha, ruta.RepartidorId, ruta.VehiculoId, ruta.Estado.ToString(), ruta.Revision, ruta.ReservaActiva,
            ruta.Paradas.OrderBy(p => p.Orden).Select(p => new ParadaRutaResponse(p.Id, p.EnvioId, p.Orden, p.Estado.ToString())).ToList(), carga);
    private sealed record DatosEvaluacion(VehiculoRuta Vehiculo, ReglasPlanificacion Reglas, IReadOnlyList<EnvioRuta> Envios, EvaluacionRuta Evaluacion);
}
