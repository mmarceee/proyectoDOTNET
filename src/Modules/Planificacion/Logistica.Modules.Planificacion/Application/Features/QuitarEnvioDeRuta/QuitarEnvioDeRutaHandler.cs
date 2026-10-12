using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Application.Mapping;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Features.QuitarEnvioDeRuta;

internal sealed class QuitarEnvioDeRutaHandler(IRutaRepository rutas, IAdministracionModuleApi administracion,
    IEnviosModuleApi envios, IConfirmacionPlanificacion confirmacion, IResponsablePlanificacion responsable, TimeProvider reloj)
{
    public async Task<RutaResponse> HandleAsync(QuitarEnvioDeRutaCommand command, CancellationToken ct)
    {
        if (command.RutaId == Guid.Empty || command.EnvioId == Guid.Empty || command.RevisionEsperada < 1)
            throw new DomainException("La ruta, el envío y la revisión son obligatorios.");

        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            // La misma exclusión que crear/agregar y, en CU-43, despachar.
            await administracion.BloquearPlanificacionAsync(ct);
            var ruta = await rutas.ObtenerAsync(command.RutaId, ct) ?? throw new RutaNoEncontradaException();
            if (ruta.Estado != EstadoRuta.Planificada)
                throw new ConflictoRutaException("RutaNoEditable", "Sólo se pueden quitar envíos de una ruta Planificada.");
            if (ruta.Revision != command.RevisionEsperada)
                throw new ConflictoRutaException("RutaModificada", "Otra operación modificó la ruta. Revisá las paradas y confirmá nuevamente.", revision: ruta.Revision);

            var parada = ruta.Paradas.SingleOrDefault(p => p.Envios.Any(e => e.EnvioId == command.EnvioId))
                ?? throw new DomainException("El envío no pertenece a la ruta.");
            var envio = (await envios.ObtenerParaPlanificacionAsync([command.EnvioId], ct)).SingleOrDefault()
                ?? throw new DomainException("El envío no está disponible.");
            var ordenAnterior = parada.Orden;
            ruta.QuitarEnvio(command.EnvioId);
            await envios.DesasignarDeRutaAsync(ruta.Id, command.EnvioId, responsable.Id, ct);
            rutas.Agregar(RetiroEnvioRuta.Registrar(ruta, command.EnvioId, envio.Numero, ordenAnterior,
                responsable.Id, reloj.GetUtcNow()));
            await rutas.GuardarAsync(ct);
            respuesta = RutaResponseMapper.Mapear(ruta);
        }, [], ct);
        return respuesta!;
    }
}
