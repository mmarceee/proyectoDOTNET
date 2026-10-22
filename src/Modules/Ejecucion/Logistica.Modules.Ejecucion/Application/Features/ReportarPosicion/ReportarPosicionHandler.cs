using System.Text.Json;
using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Ejecucion.Application.Mapping;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Ejecucion.Domain.Posiciones;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Planificacion.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logistica.Modules.Ejecucion.Application.Features.ReportarPosicion;

internal sealed class ReportarPosicionHandler(IPosicionRepository posiciones, IOperacionRepository operaciones,
    IPlanificacionModuleApi rutas, IRendicionRepository rendiciones, IContextoHojaRuta contexto,
    IOptions<RetencionPosiciones> retencion, IEnumerable<IConsumidorPosicionReportada> consumidores,
    ILogger<ReportarPosicionHandler> log)
{
    private PosicionReportada? pendiente;

    // Se llama dentro de la transacción de CU-52. El evento se entrega sólo cuando ésta ya se confirmó.
    public async Task<ResultadoOperacionResponse> HandleAsync(ProcesarOperacionCommand command, DatosOperacion datos, CancellationToken ct)
    {
        pendiente = null;
        var op = command.Operacion;
        async Task<ResultadoOperacionResponse> Rechazar(string motivo, bool conflicto = false)
        {
            operaciones.Agregar(OperacionDispositivo.Rechazada(op.OperacionId, datos,
                conflicto ? ResultadoOperacion.Conflicto : ResultadoOperacion.Invalida, motivo, null, JsonSerializer.Serialize(op)));
            await operaciones.GuardarCambiosAsync(ct);
            return new(op.OperacionId, conflicto ? "Conflicto" : "Invalida", motivo);
        }
        if (op.EnvioId is not null || op.DispositivoId == Guid.Empty || op.Secuencia < 1 || op.OcurridoEn == default
            || !PosicionVehiculo.CoordenadasValidas(op.Latitud, op.Longitud)
            || op.Carga is not null || op.Rendicion is not null || op.Entrega is not null || op.IntentoFallido is not null)
            return await Rechazar("PosicionInvalida");
        if (datos.OcurridoEn < datos.RecibidoEn.AddDays(-retencion.Value.Dias))
            return await Rechazar("PosicionFueraDeRetencion");
        if (command.UsuarioId is not Guid usuario || await contexto.ConsultarAsync(usuario, command.RepartidorId, null, ct) is null)
            return await Rechazar("RutaNoAsignada", true);
        var ruta = await rutas.ConsultarParaPosicionAsync(op.RutaId, command.RepartidorId, ct);
        if (ruta is null) return await Rechazar("RutaNoAsignada", true);
        var rendicion = await rendiciones.ObtenerAsync(op.RutaId, ct);
        var jornada = new JornadaSeguimiento(ruta.Estado == "EnCurso", ruta.Estado == "Finalizada", ruta.DespachadaEn, rendicion?.IniciadaEn);
        var validez = jornada.Evaluar(datos.OcurridoEn);
        if (validez == ValidezCaptura.RutaNoEnCurso)
            return await Rechazar("RutaNoEnCurso", true);
        if (validez == ValidezCaptura.FueraDeJornada)
            return await Rechazar("PosicionFueraDeJornada", true);
        var p = PosicionVehiculo.Crear(op.OperacionId, datos.OperadorId, ruta.VehiculoId, op.RutaId,
            op.Latitud!.Value, op.Longitud!.Value, datos.OcurridoEn, datos.RecibidoEn);
        posiciones.Agregar(p);
        operaciones.Agregar(OperacionDispositivo.Aplicada(op.OperacionId, datos));
        await operaciones.GuardarCambiosAsync(ct);
        pendiente = new(PosicionMapper.Mapear(p), jornada.SoloHistorial);
        return new(op.OperacionId, "Aplicada");
    }

    public async Task PublicarConfirmadaAsync()
    {
        var evento = pendiente;
        pendiente = null;
        if (evento is null) return;
        foreach (var consumidor in consumidores)
        {
            try { await consumidor.ConsumirAsync(evento, CancellationToken.None); }
            catch (Exception ex) { log.LogWarning(ex, "No se pudo notificar la posición {PosicionId}; ya está persistida", evento.Posicion.Id); }
        }
    }

}
