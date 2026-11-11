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
// decide con la máquina de estados de cada bulto; lo que no se puede aplicar se guarda completo, sin aplicarse.
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
    IContextoHojaRuta contexto,
    Features.ReportarPosicion.ReportarPosicionHandler posiciones,
    IIntegrationEventPublisher eventos)
{
    public const string TipoVisita = "RegistrarVisita";
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
                        : new(command.Operacion.OperacionId, Nombre(existente.Resultado), existente.Motivo, existente.Detalle,
                            existente.Bultos is null ? null : JsonSerializer.Deserialize<List<BultoRechazadoResponse>>(existente.Bultos));
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
        if (op.Tipo is "ConfirmarCarga" or "IniciarRuta" or TipoVisita or TipoEntrega or TipoIntentoFallido
            && await rendiciones.ObtenerAsync(op.RutaId, ct) is { } rendicion)
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto,
                rendicion.RepartidorId == command.RepartidorId ? "RutaEnRendicion" : "RutaNoAsignada", null, ct);
        if (op.Tipo == "ConfirmarCarga") return await cargas.HandleAsync(command, datos, ct);
        if (op.Tipo == "IniciarRuta") return await IniciarRutaAsync(command, datos, ct);

        if (op.Tipo is not (TipoVisita or TipoEntrega or TipoIntentoFallido))
        {
            return await RechazarAsync(op, datos, ResultadoOperacion.Invalida, "TipoNoSoportado", op.Tipo, ct);
        }

        if (op.EnvioId is not Guid envioId || op.Latitud is not decimal latitud || op.Longitud is not decimal longitud
            || (op.Tipo == TipoVisita && op.Visita is not { Bultos.Count: > 0 })
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

        if (await planificacion.ConsultarCargaAsync(op.RutaId, command.RepartidorId, ct) is not { Estado: "EnCurso" })
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto, "RutaNoIniciada", null, ct);

        RegistroEnCalle? registro = null;
        Diagnostico diagnostico = Diagnostico.SinRechazo;
        try
        {
            // La clasificación de cada bulto, la visita y el registro de la operación, en una sola transacción: se
            // aplican juntos o ninguno (sección 2.11).
            await unidadDeTrabajo.EjecutarAsync(async () =>
            {
                if (await envios.ConsultarParaVisitaAsync(envioId, ct, op.RutaId) is not { } envio)
                {
                    registro = new(ResultadoRegistro.EnvioNoEncontrado);
                    return;
                }

                var visita = Visita(op, envio);
                diagnostico = DiagnosticoVisita.Evaluar(visita.Bultos, envio);
                if (diagnostico.Rechazo is not null)
                {
                    return;
                }

                registro = await envios.RegistrarVisitaAsync(Registro(op, visita, envio, envioId, latitud, longitud, datos,
                    command.UsuarioId), ct);
                if (registro.Resultado == ResultadoRegistro.Aplicado)
                {
                    var entregados = visita.Bultos.Where(b => b.MotivoId is null)
                        .Select(b => envio.Bultos.Single(x => x.Codigo.Equals(b.CodigoBulto.Trim(), StringComparison.OrdinalIgnoreCase)).BultoId).ToArray();
                    await envios.RegistrarCustodiaVisitaAsync(op.RutaId, entregados, op.OperacionId, command.UsuarioId, datos.OcurridoEn, ct);
                    var revision = await planificacion.RevisionAsync(op.RutaId, ct)
                        ?? throw new DomainException("La ruta dejó de estar disponible.");
                    await planificacion.RegistrarConciliacionAsync(op.RutaId, revision, ct);
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

        if (diagnostico.Rechazo is ResultadoOperacion rechazo)
        {
            // El detalle resume el diagnóstico para el tablero del despachador; la visita lo recibe también bulto por bulto.
            var detalle = diagnostico.Bultos.Count == 0 ? null
                : string.Join("; ", diagnostico.Bultos.Select(b => $"{b.CodigoBulto}: {b.Motivo}"));
            return await RechazarAsync(op, datos, rechazo, Motivo(op, diagnostico.Motivo!), detalle, ct,
                op.Tipo == TipoVisita ? diagnostico.Bultos : null);
        }

        return registro!.Resultado switch
        {
            ResultadoRegistro.Aplicado => new(op.OperacionId, Resultados.Aplicada),
            // Otro proceso cambió el envío entre la clasificación y el registro: un conflicto más.
            ResultadoRegistro.Conflicto => await RechazarAsync(op, datos, ResultadoOperacion.Conflicto,
                Motivo(op, "EstadoIncompatible"), registro.EstadoActual, ct),
            ResultadoRegistro.Invalido => await RechazarAsync(op, datos, ResultadoOperacion.Invalida,
                Motivo(op, "EvidenciaIncompleta"), registro.Detalle, ct),
            _ => await RechazarAsync(op, datos, ResultadoOperacion.Invalida, "EnvioNoEncontrado", null, ct),
        };
    }

    // Sección 2.5: se guarda la operación completa, con su evidencia, y no se aplica. En una visita, también el
    // diagnóstico de cada bulto (sección 2.11).
    private async Task<ResultadoOperacionResponse> RechazarAsync(OperacionDispositivoRequest op, DatosOperacion datos,
        ResultadoOperacion resultado, string motivo, string? detalle, CancellationToken ct,
        IReadOnlyList<BultoRechazadoResponse>? bultos = null)
    {
        operaciones.Agregar(OperacionDispositivo.Rechazada(op.OperacionId, datos, resultado, motivo, detalle,
            JsonSerializer.Serialize(op), bultos is null ? null : JsonSerializer.Serialize(bultos)));
        await operaciones.GuardarCambiosAsync(ct);

        return new(op.OperacionId, Nombre(resultado), motivo, detalle, bultos);
    }

    // Una entrega o un intento fallido anteriores a la entrega parcial valían para todos los bultos del envío: se
    // traducen a una visita con el mismo resultado para cada uno (sección 2.11). Si algún bulto ya no está en tránsito,
    // la visita se clasifica como cualquier otra, sin inventar un resultado.
    private static VisitaRequest Visita(OperacionDispositivoRequest op, EnvioParaVisita envio)
    {
        if (op.Tipo == TipoVisita)
        {
            return op.Visita!;
        }

        if (op.Tipo == TipoEntrega)
        {
            var entrega = op.Entrega!;
            return new VisitaRequest(envio.Bultos.Select(b => new ResultadoBultoRequest(b.Codigo)).ToList(), entrega.Firma,
                entrega.Foto, entrega.NombreReceptor, entrega.DocumentoReceptor);
        }

        var intento = op.IntentoFallido!;
        return new VisitaRequest(envio.Bultos.Select(b => new ResultadoBultoRequest(b.Codigo, intento.MotivoId)).ToList(),
            Foto: intento.Foto, Observaciones: intento.Observaciones);
    }

    // La PWA anterior a la entrega parcial conoce los motivos por envío: sus operaciones los conservan (sección 2.4).
    private static string Motivo(OperacionDispositivoRequest op, string motivo)
    {
        if (op.Tipo == TipoVisita)
        {
            return motivo;
        }

        return motivo switch
        {
            "BultoExtraviado" => "EnvioExtraviado",
            "BultoEnDeposito" => "EnvioEnDeposito",
            "BultoYaResuelto" => "EnvioYaResuelto",
            "EvidenciaIncompleta" => "PruebaInvalida",
            "BultoFueraDeRuta" => "EstadoIncompatible",
            _ => motivo,
        };
    }

    private async Task<ResultadoOperacionResponse> IniciarRutaAsync(ProcesarOperacionCommand command, DatosOperacion datos,
        CancellationToken ct)
    {
        var op = command.Operacion;
        if (op.EnvioId is not null || op.DispositivoId == Guid.Empty || op.Secuencia < 1 || op.OcurridoEn == default)
            return await RechazarAsync(op, datos, ResultadoOperacion.Invalida, "DatosIncompletos", null, ct);
        var identidad = command.UsuarioId is Guid usuario
            ? await contexto.ConsultarAsync(usuario, command.RepartidorId, null, ct) : null;
        var ruta = await planificacion.ConsultarCargaAsync(op.RutaId, command.RepartidorId, ct);
        if (identidad is null || ruta is null)
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto, "RutaNoAsignada", null, ct);
        if (ruta.Estado != "Despachada")
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto, "RutaYaIniciada", null, ct);
        var evidencia = await rendiciones.EvidenciaAsync(op.RutaId, ct);
        if (evidencia.Operaciones.Any(o => o.Tipo == "ConfirmarCarga" && o.Resultado != ResultadoOperacion.Aplicada && !o.Revisada))
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto, "DependeDeOperacionRechazada", null, ct);
        var fecha = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(datos.OcurridoEn,
            TimeZoneInfo.FindSystemTimeZoneById(identidad.ZonaHoraria)).DateTime);
        if (ruta.Fecha != fecha)
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto, "FechaDeInicioInvalida", null, ct);
        var lista = await envios.ConsultarCargaAsync(ruta.Envios.ToArray(), ct);
        if (lista.Count != ruta.Envios.Count || !lista.Any(e => e.Bultos.Any(b => b.Estado == "EnTransito")))
            return await RechazarAsync(op, datos, ResultadoOperacion.Conflicto, "SinCargaConfirmada", null, ct);
        // El bloqueo por operador y la transacción exterior serializan inicio, cargas y visitas.
        await planificacion.IniciarRutaAsync(op.RutaId, ct);
        operaciones.Agregar(OperacionDispositivo.Aplicada(op.OperacionId, datos, JsonSerializer.Serialize(op)));
        await operaciones.GuardarCambiosAsync(ct);
        return new(op.OperacionId, Resultados.Aplicada);
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

    // La visita ya clasificada: cada código de bulto es de este envío.
    private static VisitaARegistrar Registro(OperacionDispositivoRequest op, VisitaRequest visita, EnvioParaVisita envio,
        Guid envioId, decimal latitud, decimal longitud, DatosOperacion datos, Guid? usuarioId)
    {
        var porCodigo = envio.Bultos.ToDictionary(b => b.Codigo, b => b.BultoId, StringComparer.OrdinalIgnoreCase);
        return new VisitaARegistrar(envioId, datos.OcurridoEn, latitud, longitud,
            visita.Bultos.Select(b => new ResultadoBultoARegistrar(porCodigo[b.CodigoBulto.Trim()], b.MotivoId)).ToList(),
            visita.Firma is null ? null : Archivo(visita.Firma), visita.Foto is null ? null : Archivo(visita.Foto),
            visita.NombreReceptor, visita.DocumentoReceptor, visita.Observaciones, usuarioId, datos.RepartidorId, op.RutaId);
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
