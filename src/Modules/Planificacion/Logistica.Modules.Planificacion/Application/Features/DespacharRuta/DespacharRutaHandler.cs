using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Application.Services;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;
using static Logistica.Modules.Planificacion.Application.Mapping.RutaResponseMapper;
using static Logistica.Modules.Planificacion.Application.Services.EvaluadorRuta;

namespace Logistica.Modules.Planificacion.Application.Features.DespacharRuta;

internal sealed class DespacharRutaHandler(IRutaRepository rutas, IAdministracionModuleApi administracion,
    EvaluadorRuta evaluador, IConfirmacionPlanificacion confirmacion, IIntegrationEventPublisher eventos,
    IResponsablePlanificacion responsable, TimeProvider reloj)
{
    public async Task<EvaluacionRutaResponse> PrevalidarAsync(DespacharRutaCommand command, CancellationToken ct)
    {
        var ruta = await ExigirRutaAsync(command, ct);
        var datos = await EvaluarAsync(ruta, ct);
        return Resumen(datos.Evaluacion, datos.Vehiculo, datos.Reglas);
    }

    public async Task<RutaResponse> HandleAsync(DespacharRutaCommand command, CancellationToken ct)
    {
        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            // Misma exclusión que CU-40 y CU-41, para revalidar y confirmar juntos.
            await administracion.BloquearPlanificacionAsync(ct);
            var ruta = await ExigirRutaAsync(command, ct);
            var datos = await EvaluarAsync(ruta, ct);
            var ahora = reloj.GetUtcNow();
            ruta.Despachar(ahora);
            rutas.Agregar(ValidacionRuta.Registrar(ruta, OperacionValidacionRuta.Despacho, datos.Vehiculo,
                datos.Reglas.VersionId, datos.Reglas.MaxParadasPorRuta, datos.Envios, responsable.Id, ahora));
            eventos.Registrar(RutaDespachada.Tipo,
                new RutaDespachada(ruta.Id, ruta.RepartidorId, ruta.Fecha, ruta.Revision, responsable.Id), ahora);
            await rutas.GuardarAsync(ct);
            respuesta = Mapear(ruta, Resumen(datos.Evaluacion, datos.Vehiculo, datos.Reglas));
        }, [], ct);
        return respuesta!;
    }

    private async Task<DatosEvaluacion> EvaluarAsync(Ruta ruta, CancellationToken ct)
    {
        var datos = await evaluador.CargarAsync(ruta.Fecha, ruta.RepartidorId, ruta.VehiculoId,
            ruta.EnvioIds.ToArray(), [], ruta.Id, ct);
        if (!datos.Repartidor.TieneUsuarioVinculado)
            throw new DomainException($"No se puede despachar la ruta: {datos.Repartidor.Nombre} no tiene un usuario vinculado para acceder a la PWA. Vinculá su cuenta o elegí otro repartidor antes de despachar.");
        ExigirValida(datos.Evaluacion);
        return datos;
    }

    private async Task<Ruta> ExigirRutaAsync(DespacharRutaCommand command, CancellationToken ct)
    {
        if (command.RutaId == Guid.Empty || command.RevisionEsperada < 1)
            throw new DomainException("La ruta y la revisión son obligatorias.");
        var ruta = await rutas.ObtenerAsync(command.RutaId, ct) ?? throw new RutaNoEncontradaException();
        if (ruta.Estado != EstadoRuta.Planificada)
            throw new ConflictoRutaException("RutaNoEditable", "La ruta ya no está Planificada.");
        if (ruta.Revision != command.RevisionEsperada)
            throw new ConflictoRutaException("RutaModificada", "Otra operación modificó la ruta. Recargá y confirmá nuevamente.", revision: ruta.Revision);
        if (ruta.Paradas.Count == 0) throw new DomainException("No se puede despachar una ruta vacía.");
        return ruta;
    }
}
