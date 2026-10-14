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
using static Logistica.Modules.Planificacion.Application.Services.EvaluadorRuta;

namespace Logistica.Modules.Planificacion.Application.Features.ArmarRuta;

internal sealed class ArmarRutaHandler(IRutaRepository rutas, IAdministracionModuleApi administracion,
    IEnviosModuleApi envios, EvaluadorRuta evaluador, IConfirmacionPlanificacion confirmacion,
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
        var todos = (ruta?.EnvioIds ?? []).Concat(pedido.EnvioIds).ToArray();
        if (todos.Distinct().Count() != todos.Length) throw new DomainException("La selección incluye un envío ya presente o repetido.");
        var datos = await evaluador.CargarAsync(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId, todos,
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
            var datos = await evaluador.CargarAsync(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId, pedido.EnvioIds,
                pedido.EnvioIds, null, ct);
            ExigirValida(datos.Evaluacion);
            var ruta = Ruta.Crear(Operador, pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId, datos.Envios, reloj.GetUtcNow());
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
            var todos = ruta.EnvioIds.Concat(pedido.EnvioIds).ToArray();
            if (todos.Distinct().Count() != todos.Length) throw new DomainException("Algún envío ya pertenece a la ruta.");
            var datos = await evaluador.CargarAsync(ruta.Fecha, ruta.RepartidorId, ruta.VehiculoId, todos, pedido.EnvioIds, ruta.Id, ct);
            ExigirValida(datos.Evaluacion);
            ruta.AgregarEnvios(datos.Envios);
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
            var datos = await evaluador.CargarAsync(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId,
                ruta.EnvioIds.ToArray(), [], ruta.Id, ct);
            ExigirValida(datos.Evaluacion);
            var anterior = ruta.Revision;
            ruta.ModificarPlanificacion(pedido.Fecha, pedido.RepartidorId, pedido.VehiculoId, datos.Envios);
            if (ruta.Revision != anterior) await GuardarValidacionAsync(ruta, OperacionValidacionRuta.Modificacion, datos, ct);
            respuesta = Mapear(ruta, Resumen(datos.Evaluacion, datos.Vehiculo, datos.Reglas));
        }, [], ct);
        return respuesta!;
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
}
