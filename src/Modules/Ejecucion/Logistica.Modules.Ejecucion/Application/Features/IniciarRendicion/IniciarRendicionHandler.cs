using System.Text.Json;
using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Application.Features.IniciarRendicion;

internal sealed class IniciarRendicionHandler(IPlanificacionModuleApi rutas, IContextoHojaRuta contexto,
    IRendicionRepository rendiciones, IOperacionRepository operaciones, PrepararRendicion preparar)
{
    // El coordinador de operaciones abre la transacción y toma el bloqueo del operador antes de entrar aquí.
    public async Task<ResultadoOperacionResponse> HandleAsync(ProcesarOperacionCommand command, DatosOperacion datos, CancellationToken ct)
    {
        var op = command.Operacion;
        async Task<ResultadoOperacionResponse> Rechazar(string motivo, bool invalida = false)
        {
            operaciones.Agregar(OperacionDispositivo.Rechazada(op.OperacionId, datos,
                invalida ? ResultadoOperacion.Invalida : ResultadoOperacion.Conflicto, motivo, null, JsonSerializer.Serialize(op)));
            await operaciones.GuardarCambiosAsync(ct);
            return new(op.OperacionId, invalida ? "Invalida" : "Conflicto", motivo);
        }
        var declaracion = op.Rendicion;
        if (op.EnvioId is not null || op.DispositivoId == Guid.Empty || op.Secuencia < 1 || declaracion is null
            || declaracion.Bultos is null || declaracion.Bultos.Count > 2000 || declaracion.Dependencias is null
            || declaracion.Dependencias.Count > 2000 || declaracion.Dependencias.Any(id => id == Guid.Empty || id == op.OperacionId)
            || declaracion.Dependencias.Distinct().Count() != declaracion.Dependencias.Count
            || declaracion.Bultos.Any(b => b is null || b.BultoId == Guid.Empty || b.Observacion?.Length > 1000)
            || declaracion.Bultos.Select(b => b.BultoId).Distinct().Count() != declaracion.Bultos.Count
            || declaracion.Observaciones?.Length > 2000)
            return await Rechazar("RendicionInvalida", true);
        if (command.UsuarioId is not Guid usuario || await contexto.ConsultarAsync(usuario, command.RepartidorId, null, ct) is null)
            return await Rechazar("RutaNoAsignada");
        var ruta = await rutas.ConsultarCargaAsync(op.RutaId, command.RepartidorId, ct);
        if (ruta is null) return await Rechazar("RutaNoAsignada");
        if (ruta.Estado != "EnCurso") return await Rechazar("RutaNoEnCurso");
        if (await rendiciones.ObtenerAsync(op.RutaId, ct) is not null) return await Rechazar("RendicionYaIniciada");
        var evidencia = await rendiciones.EvidenciaAsync(op.RutaId, ct);
        // También cubre rechazos de lotes anteriores. Revisarlos corresponde al flujo de conflictos de CU-66.
        if (evidencia.Operaciones.Any(o => o.Resultado != ResultadoOperacion.Aplicada && !o.Revisada
                && o.Tipo is "ConfirmarCarga" or "RegistrarEntrega" or "RegistrarIntentoFallido"))
            return await Rechazar("DependeDeOperacionRechazada");
        if (declaracion.Dependencias.Any(id => !evidencia.Operaciones.Any(o => o.Id == id
                && o.RepartidorId == command.RepartidorId && o.Resultado == ResultadoOperacion.Aplicada)))
            return await Rechazar("OperacionPreviaPendiente");
        var baseActual = await preparar.ConsultarAsync(op.RutaId, ruta.Envios, ct);
        var porId = declaracion.Bultos.ToDictionary(b => b.BultoId);
        if (baseActual.Bultos.Any(b => b.Situacion == "Revisar") || porId.Count != baseActual.Bultos.Count
            || baseActual.Bultos.Any(b => !porId.TryGetValue(b.BultoId, out var d) || d.Situacion != b.Situacion))
            return await Rechazar("ManifiestoModificado");
        Rendicion rendicion;
        try
        {
            rendicion = Rendicion.Iniciar(datos.OperadorId, op.RutaId, command.RepartidorId, op.OperacionId,
                datos.OcurridoEn, declaracion.Observaciones, baseActual.Bultos.Select(b =>
                    new DeclaracionRendicion(b.EnvioId, b.BultoId, b.Situacion, porId[b.BultoId].Devuelve, porId[b.BultoId].Observacion)).ToArray());
        }
        catch (DomainException) { return await Rechazar("DeclaracionIncompleta", true); }
        rendiciones.Agregar(rendicion);
        await rendiciones.GuardarAsync(ct);
        operaciones.Agregar(OperacionDispositivo.Aplicada(op.OperacionId, datos));
        await operaciones.GuardarCambiosAsync(ct);
        return new(op.OperacionId, "Aplicada");
    }
}
