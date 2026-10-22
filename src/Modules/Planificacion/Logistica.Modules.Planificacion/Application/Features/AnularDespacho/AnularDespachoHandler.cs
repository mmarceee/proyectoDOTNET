using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;
using static Logistica.Modules.Planificacion.Application.Mapping.RutaResponseMapper;

namespace Logistica.Modules.Planificacion.Application.Features.AnularDespacho;

internal sealed class AnularDespachoHandler(IRutaRepository rutas, IAdministracionModuleApi administracion,
    IEjecucionModuleApi ejecucion, IEnviosModuleApi envios, IConfirmacionPlanificacion confirmacion,
    IResponsablePlanificacion responsable, IIntegrationEventPublisher eventos,
    TimeProvider reloj)
{
    public async Task<RutaResponse> HandleAsync(AnularDespachoCommand command, CancellationToken ct)
    {
        if (command.RutaId == Guid.Empty || command.RevisionEsperada < 1
            || string.IsNullOrWhiteSpace(command.Motivo) || command.Motivo.Trim().Length > 1000)
            throw new DomainException("Indicá la ruta, su revisión y un motivo de hasta 1000 caracteres.");
        if (!command.ConfirmoSinCargaPendiente)
            throw new DomainException("Confirmá con el repartidor que no hay carga física ni carga pendiente de sincronizar.");
        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            await administracion.BloquearPlanificacionAsync(ct);
            var ruta = await rutas.ObtenerAsync(command.RutaId, ct) ?? throw new RutaNoEncontradaException();
            if (ruta.Revision != command.RevisionEsperada)
                throw new ConflictoRutaException("RutaModificada", "Otra operación modificó la ruta. Recargá antes de anular.", revision: ruta.Revision);
            if (ruta.Estado != EstadoRuta.Despachada || ruta.DespachadaEn is null)
                throw new ConflictoRutaException("RutaIniciada", "La ruta debe seguir Despachada y sin iniciar.");
            if (await ejecucion.TieneActividadRegistradaAsync(ruta.Id, ct))
                throw new ConflictoRutaException("RutaConActividad", "Hay carga, rendición u operaciones aplicadas. No se puede anular el despacho.");
            var ids = ruta.EnvioIds.ToArray();
            var lista = await envios.ConsultarCargaAsync(ids, ct);
            if (lista.Count != ids.Length || lista.Any(e => e.Estado != "AsignadoARuta"))
                throw new ConflictoRutaException("EnvioNoDisponible", "Los envíos deben seguir asignados a esta ruta y sin salir del depósito.");
            var despacho = ruta.DespachadaEn.Value;
            var anterior = ruta.Revision;
            var ahora = reloj.GetUtcNow();
            ruta.AnularDespacho();
            rutas.Agregar(AnulacionDespacho.Registrar(ruta, despacho, anterior, command.Motivo.Trim(), responsable.Id, ahora));
            eventos.Registrar(DespachoAnulado.Tipo,
                new DespachoAnulado(ruta.Id, ruta.RepartidorId, ruta.Fecha, ruta.Revision, responsable.Id), ahora);
            await rutas.GuardarAsync(ct);
            respuesta = Mapear(ruta);
        }, [], ct);
        return respuesta!;
    }
}
