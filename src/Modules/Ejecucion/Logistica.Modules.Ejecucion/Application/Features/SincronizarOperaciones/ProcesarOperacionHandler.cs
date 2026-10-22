using System.Text.Json;
using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.SharedKernel;
using Logistica.Modules.Ejecucion.Application.Abstractions;
using Logistica.Modules.Ejecucion.Contracts.Events;

namespace Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;

// Una operación de la PWA (ADR-0004): idempotencia, hora, pertenencia, validez y transición, en ese orden. El servidor
// decide con la máquina de estados del envío; lo que no se puede aplicar se guarda completo, sin aplicarse.
internal sealed class ProcesarOperacionHandler(
    IOperacionRepository operaciones,
    IEnviosModuleApi envios,
    IPlanificacionModuleApi planificacion,
    IUnidadDeTrabajo unidadDeTrabajo,
    ICurrentTenant tenant,
    TimeProvider reloj,
    Features.ConfirmarCarga.ConfirmarCargaHandler cargas,
    Features.IniciarRendicion.IniciarRendicionHandler rendir,
    IRendicionRepository rendiciones,
    IAdministracionModuleApi administracion,
    Features.ReportarPosicion.ReportarPosicionHandler posiciones,
    IIntegrationEventPublisher eventos)
{
    public const string TipoEntrega = "RegistrarEntrega";
    public const string TipoIntentoFallido = "RegistrarIntentoFallido";

    // Un reloj más adelantado que esto respecto del servidor se corrige (sección 2.7).
    private static readonly TimeSpan ToleranciaReloj = TimeSpan.FromMinutes(2);

    public async Task<ResultadoOperacionResponse> HandleAsync(ProcesarOperacionCommand command, CancellationToken ct)
    {
        ResultadoOperacionResponse? resultado = null;
        await unidadDeTrabajo.EjecutarAsync(async () =>
        {
            // El cierre de jornada y sus operaciones anteriores no pueden adelantarse mutuamente.
            await administracion.BloquearPlanificacionAsync(ct);
            // ADR-0004 §2.2: un reenvío devuelve el resultado y no genera otro aviso de conflicto.
            if (await operaciones.ObtenerAsync(command.Operacion.OperacionId, ct) is { } existente)
            {
                resultado = existente.RepartidorId != command.RepartidorId
                    ? new(command.Operacion.OperacionId, Resultados.Conflicto, "RutaNoAsignada")
                    : existente.Resultado == ResultadoOperacion.Aplicada
                        ? new(command.Operacion.OperacionId, Resultados.YaAplicada)
                        : new(command.Operacion.OperacionId, Nombre(existente.Resultado), existente.Motivo, existente.Detalle);
                return;
            }
            resultado = await ProcesarAsync(command, ct);
            if (resultado.Resultado is Resultados.Conflicto or Resultados.Invalida)
            {
                var op = command.Operacion;
                eventos.Registrar(ConflictoSincronizacionRegistrado.Tipo,
                    new ConflictoSincronizacionRegistrado(op.OperacionId, op.RutaId, op.EnvioId,
                        command.RepartidorId, resultado.Resultado, resultado.Motivo), reloj.GetUtcNow());
                await operaciones.GuardarCambiosAsync(ct);
            }
        }, ct);
        await posiciones.PublicarConfirmadaAsync();
        return resultado!;
    }

    private async Task<ResultadoOperacionResponse> ProcesarAsync(ProcesarOperacionCommand command, CancellationToken ct)
    {
        var op = command.Operacion;

        var datos = Datos(command);

        if (command.EnvioConRechazoPrevio)
        {
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto, "DependeDeOperacionRechazada", null, ct);
        }

        if (op.Tipo == "IniciarRendicion") return await rendir.HandleAsync(command, datos, ct);
        if (op.Tipo == "ReportarPosicion") return await posiciones.HandleAsync(command, datos, ct);
        if (op.Tipo is "ConfirmarCarga" or TipoEntrega or TipoIntentoFallido
            && await rendiciones.ObtenerAsync(op.RutaId, ct) is { } rendicion)
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto,
                rendicion.RepartidorId == command.RepartidorId ? "RutaEnRendicion" : "RutaNoAsignada", null, ct);
        if (op.Tipo == "ConfirmarCarga") return await cargas.HandleAsync(command, datos, ct);

        if (op.Tipo is not (TipoEntrega or TipoIntentoFallido))
        {
            return await RechazarAsync(op, datos, ResultadoOperacion.Invalida, "TipoNoSoportado", op.Tipo, ct);
        }

        if (op.EnvioId is not Guid envioId || op.Latitud is not decimal latitud || op.Longitud is not decimal longitud
            || (op.Tipo == TipoEntrega && op.Entrega is null) || (op.Tipo == TipoIntentoFallido && op.IntentoFallido is null))
        {
            return await RechazarAsync(op, datos, ResultadoOperacion.Invalida, "DatosIncompletos",
                "Faltan el envío, la posición o los datos de la operación.", ct);
        }

        // Sección 2.3, paso 1: la ruta es del repartidor de la sesión, ya salió, y el envío está en ella.
        if (!await planificacion.EnvioEnRutaDelRepartidorAsync(op.RutaId, command.RepartidorId, envioId, ct))
        {
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto, "RutaNoAsignada", null, ct);
        }

        RegistroEnCalle? registro = null;
        try
        {
            // Pasos 2 a 4, y el registro de la operación, en una sola transacción: se aplican juntos o ninguno.
            await unidadDeTrabajo.EjecutarAsync(async () =>
            {
                registro = op.Tipo == TipoEntrega
                    ? await envios.RegistrarEntregaAsync(Entrega(op, envioId, latitud, longitud, datos, command.UsuarioId), ct)
                    : await envios.RegistrarIntentoFallidoAsync(Intento(op, envioId, latitud, longitud, datos, command.UsuarioId), ct);

                if (registro.Resultado == ResultadoRegistro.Aplicado)
                {
                    operaciones.Agregar(OperacionDispositivo.Aplicada(op.OperacionId, datos));
                    await operaciones.GuardarCambiosAsync(ct);
                }
            }, ct);
        }
        catch (FormatException)
        {
            // La firma o la foto no son base64 válido.
            return await RechazarAsync(op, datos, ResultadoOperacion.Invalida, "ArchivoInvalido", null, ct);
        }

        return registro!.Resultado switch
        {
            ResultadoRegistro.Aplicado => new(op.OperacionId, Resultados.Aplicada),
            ResultadoRegistro.Conflicto => await RechazarAsync(op, datos, ResultadoOperacion.Conflicto,
                MotivoDeConflicto(registro.EstadoActual), registro.EstadoActual, ct),
            ResultadoRegistro.Invalido => await RechazarAsync(op, datos, ResultadoOperacion.Invalida,
                "PruebaInvalida", registro.Detalle, ct),
            _ => await RechazarAsync(op, datos, ResultadoOperacion.Invalida, "EnvioNoEncontrado", null, ct),
        };
    }

    // Sección 2.5: se guarda la operación completa, con su evidencia, y no se aplica.
    private async Task<ResultadoOperacionResponse> RechazarAsync(OperacionDispositivoRequest op, DatosOperacion datos,
        ResultadoOperacion resultado, string motivo, string? detalle, CancellationToken ct)
    {
        operaciones.Agregar(OperacionDispositivo.Rechazada(op.OperacionId, datos, resultado, motivo, detalle,
            JsonSerializer.Serialize(op)));
        await operaciones.GuardarCambiosAsync(ct);

        return new(op.OperacionId, Nombre(resultado), motivo, detalle);
    }

    // Sección 2.4: el estado del servidor dice por qué la operación ya no se puede aplicar.
    private static string MotivoDeConflicto(string? estadoActual)
    {
        return estadoActual switch
        {
            "Extraviado" => "EnvioExtraviado",
            "EnDeposito" => "EnvioEnDeposito",
            "Entregado" or "NoEntregado" or "Reprogramado" or "EnDevolucion" or "Devuelto" => "EnvioYaResuelto",
            _ => "EstadoIncompatible",
        };
    }

    private DatosOperacion Datos(ProcesarOperacionCommand command)
    {
        var op = command.Operacion;
        var recibidoEn = reloj.GetUtcNow();

        // Sección 2.7: la hora del dispositivo, salvo que esté adelantada respecto del servidor.
        var adelantada = op.OcurridoEn > recibidoEn + ToleranciaReloj;
        var operadorId = tenant.OperadorId
            ?? throw new TenantMismatchException("La sincronización necesita un operador en la sesión.");

        return new DatosOperacion(operadorId, command.RepartidorId, op.DispositivoId, op.Secuencia, op.Tipo, op.RutaId,
            op.EnvioId, adelantada ? recibidoEn : op.OcurridoEn, recibidoEn, adelantada);
    }

    private static EntregaARegistrar Entrega(OperacionDispositivoRequest op, Guid envioId, decimal latitud, decimal longitud,
        DatosOperacion datos, Guid? usuarioId)
    {
        var entrega = op.Entrega!;
        return new EntregaARegistrar(envioId, datos.OcurridoEn, latitud, longitud, Archivo(entrega.Firma),
            entrega.Foto is null ? null : Archivo(entrega.Foto), entrega.NombreReceptor, entrega.DocumentoReceptor, usuarioId);
    }

    private static IntentoFallidoARegistrar Intento(OperacionDispositivoRequest op, Guid envioId, decimal latitud,
        decimal longitud, DatosOperacion datos, Guid? usuarioId)
    {
        var intento = op.IntentoFallido!;
        return new IntentoFallidoARegistrar(envioId, datos.OcurridoEn, latitud, longitud, intento.MotivoId,
            intento.Foto is null ? null : Archivo(intento.Foto), intento.Observaciones, usuarioId);
    }

    private static ArchivoRecibido Archivo(ArchivoRequest archivo)
    {
        return new ArchivoRecibido(archivo.TipoContenido, Convert.FromBase64String(archivo.ContenidoBase64));
    }

    private static string Nombre(ResultadoOperacion resultado)
    {
        return resultado switch
        {
            ResultadoOperacion.Aplicada => Resultados.Aplicada,
            ResultadoOperacion.Conflicto => Resultados.Conflicto,
            _ => Resultados.Invalida,
        };
    }
}
