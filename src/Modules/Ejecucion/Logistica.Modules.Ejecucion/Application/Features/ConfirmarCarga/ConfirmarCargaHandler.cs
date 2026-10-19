using System.Text.Json;
using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Ejecucion.Domain.Cargas;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Application.Features.ConfirmarCarga;

internal sealed class ConfirmarCargaHandler(IUnidadDeTrabajo unidad, IAdministracionModuleApi administracion,
    IContextoHojaRuta contexto, IPlanificacionModuleApi rutas, IEnviosModuleApi envios, IRegistroCarga registro,
    IOperacionRepository operaciones, TimeProvider reloj)
{
    public async Task<ResultadoOperacionResponse> HandleAsync(ProcesarOperacionCommand command, DatosOperacion datos, CancellationToken ct)
    {
        var op = command.Operacion;
        ResultadoOperacionResponse? resultado = null;
        await unidad.EjecutarAsync(async () =>
        {
            // Mismo bloqueo por operador que crear/modificar/despachar: reserva y revisión no pueden cambiar a mitad.
            await administracion.BloquearPlanificacionAsync(ct);
            var existente = await operaciones.ObtenerAsync(op.OperacionId, ct);
            if (existente is not null)
            {
                resultado = existente.RepartidorId != command.RepartidorId
                    ? new(op.OperacionId, "Conflicto", "RutaNoAsignada")
                    : new(op.OperacionId, existente.Resultado == ResultadoOperacion.Aplicada ? "YaAplicada" : existente.Resultado.ToString(), existente.Motivo, existente.Detalle);
                return;
            }
            async Task Rechazar(string motivo, bool conflicto = false)
            {
                operaciones.Agregar(OperacionDispositivo.Rechazada(op.OperacionId, datos,
                    conflicto ? ResultadoOperacion.Conflicto : ResultadoOperacion.Invalida, motivo, null, JsonSerializer.Serialize(op)));
                await operaciones.GuardarCambiosAsync(ct);
                resultado = new(op.OperacionId, conflicto ? "Conflicto" : "Invalida", motivo);
            }
            var carga = op.Carga;
            if (op.EnvioId is not null || op.DispositivoId == Guid.Empty || op.Secuencia < 1 || carga is null
                || carga.RevisionRuta < 1 || carga.Lecturas is null || carga.Lecturas.Count is < 1 or > 2000
                || carga.Lecturas.Any(l => l is null || string.IsNullOrWhiteSpace(l.CodigoBulto) || l.CodigoBulto.Length > 200
                    || l.CodigoBulto != l.CodigoBulto.Trim() || l.EscaneadoEn == default || l.EscaneadoEn > op.OcurridoEn)
                || carga.Lecturas.Select(l => l.CodigoBulto).Distinct(StringComparer.Ordinal).Count() != carga.Lecturas.Count)
            { await Rechazar("CargaInvalida"); return; }
            var identidad = command.UsuarioId is Guid usuario ? await contexto.ConsultarAsync(usuario, command.RepartidorId, null, ct) : null;
            if (identidad is null)
            { await Rechazar("RutaNoAsignada", true); return; }
            var ruta = await rutas.ConsultarCargaAsync(op.RutaId, command.RepartidorId, ct);
            if (ruta is null) { await Rechazar("RutaNoAsignada", true); return; }
            // Se valida cuándo se confirmó, no cuándo llega el lote: la cola puede sincronizar otro día.
            var fechaCarga = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(datos.OcurridoEn,
                TimeZoneInfo.FindSystemTimeZoneById(identidad.ZonaHoraria)).DateTime);
            if (ruta.Fecha != fechaCarga) { await Rechazar("FechaDeCargaInvalida", true); return; }
            if (ruta.Revision != carga.RevisionRuta) { await Rechazar("RutaModificada", true); return; }
            var lista = await envios.ConsultarCargaAsync(ruta.Envios.ToArray(), ct);
            if (lista.Count != ruta.Envios.Count) { await Rechazar("EnvioNoEncontrado", true); return; }
            var codigos = carga.Lecturas.Select(l => l.CodigoBulto).ToHashSet(StringComparer.Ordinal);
            if (lista.Any(e => e.Estado != "AsignadoARuta" && e.Bultos.Any(b => codigos.Contains(b.Codigo))))
            { await Rechazar("EstadoIncompatible", true); return; }
            var candidatos = lista.Where(e => e.Estado == "AsignadoARuta").ToArray();
            var completos = candidatos.Where(e => e.Bultos.Count > 0 && e.Bultos.All(b => codigos.Contains(b.Codigo))).Select(e => e.Id).ToArray();
            var incompletos = candidatos.Where(e => !completos.Contains(e.Id)).Select(e => e.Id).ToArray();
            if (incompletos.Length > 0 && !carga.AceptaFaltantes) { await Rechazar("FaltantesSinConfirmar"); return; }
            var esperados = candidatos.SelectMany(e => e.Bultos).ToDictionary(b => b.Codigo, b => b.Id, StringComparer.Ordinal);
            var sobrantes = codigos.Where(c => !esperados.ContainsKey(c)).ToArray();
            var reconocidos = (await envios.ReconocerBultosAsync(sobrantes, ct)).ToDictionary(b => b.Codigo, b => b.Id, StringComparer.Ordinal);
            if (!await envios.RegistrarCargaAsync(completos, command.UsuarioId, datos.OcurridoEn, ct))
            { await Rechazar("EstadoIncompatible", true); return; }
            var escaneos = carga.Lecturas.Select(l => EscaneoCarga.Crear(datos.OperadorId, op.OperacionId, op.RutaId,
                esperados.TryGetValue(l.CodigoBulto, out var id) ? id : reconocidos.GetValueOrDefault(l.CodigoBulto) is var otro && otro != Guid.Empty ? otro : null,
                l.CodigoBulto, esperados.ContainsKey(l.CodigoBulto),
                l.EscaneadoEn > reloj.GetUtcNow().AddMinutes(2) ? datos.OcurridoEn : l.EscaneadoEn)).ToArray();
            await registro.GuardarAsync(escaneos, incompletos.Length + sobrantes.Length == 0 ? null
                : new CargaConDiscrepancias(op.OperacionId, op.RutaId, command.RepartidorId, incompletos, sobrantes), datos.OcurridoEn, ct);
            await rutas.RegistrarCargaAsync(op.RutaId, completos.Length > 0, ct);
            operaciones.Agregar(OperacionDispositivo.Aplicada(op.OperacionId, datos));
            await operaciones.GuardarCambiosAsync(ct);
            resultado = new(op.OperacionId, "Aplicada");
        }, ct);
        return resultado!;
    }
}
