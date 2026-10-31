using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.Modules.Seguimiento.Domain.Tablero;

namespace Logistica.Modules.Seguimiento.Application.Features.VerTablero;

// CU-66, paso 2: la foto completa del tablero. La arma con los contratos de cada módulo (decisión G2: sin tabla de
// lectura propia); todos ven sólo el operador de la sesión por el filtro "Tenant". La página la pide al abrir y cada
// vez que se reconecta (A1).
internal sealed class ConsultarTableroHandler(
    IPlanificacionModuleApi planificacion, IEnviosModuleApi envios, IEjecucionModuleApi ejecucion,
    IAdministracionModuleApi administracion, TimeProvider reloj)
{
    public async Task<TableroResponse> HandleAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var zona = TimeZoneInfo.FindSystemTimeZoneById(await administracion.ObtenerZonaHorariaActualAsync(ct));
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona).DateTime);

        // 1. Las rutas en la calle y los envíos que hacen falta: los de esas rutas y los pendientes hasta hoy.
        var rutas = await planificacion.ListarEnCursoAsync(ct);
        var deRutas = rutas.SelectMany(r => r.EnvioIds).ToHashSet();
        var todos = await envios.ListarParaTableroAsync(deRutas, hoy, ct);
        var porId = todos.ToDictionary(e => e.EnvioId);

        // 2. Conflictos sin revisar (ADR-0004, sección 2.5), posiciones y nombres.
        var conflictos = await ejecucion.ListarConflictosPendientesAsync(ct);
        var vehiculos = rutas.Select(r => r.VehiculoId).Distinct().ToArray();
        var repartidores = rutas.Select(r => r.RepartidorId).Concat(conflictos.Select(c => c.RepartidorId)).Distinct().ToArray();
        var posiciones = vehiculos.Length == 0 ? []
            : (await ejecucion.ConsultarUltimasPosicionesAsync(vehiculos, ct)).ToDictionary(p => p.VehiculoId);
        var nombres = repartidores.Length == 0 ? null
            : await administracion.ConsultarNombresRecursosAsync(repartidores, vehiculos, ct);

        var filas = rutas.Select(r =>
        {
            var suyos = r.EnvioIds.Select(porId.GetValueOrDefault).OfType<EnvioParaTablero>().ToList();
            var posicion = posiciones.GetValueOrDefault(r.VehiculoId) is { } p
                ? new PosicionTableroResponse(p.Id, p.VehiculoId, p.RutaId, p.Latitud, p.Longitud, p.CapturadaEn)
                : null;
            return new RutaTableroResponse(r.RutaId, r.Estado,
                nombres?.Repartidores.GetValueOrDefault(r.RepartidorId) ?? "Sin nombre",
                nombres?.Vehiculos.GetValueOrDefault(r.VehiculoId) ?? "Sin nombre", r.VehiculoId,
                r.EnvioIds.Count, suyos.Count(e => e.Estado == "Entregado"), suyos.Count(e => e.EntregaPendiente), posicion);
        }).ToList();

        // 3. Envíos en riesgo (requerimiento especial 3). La franja se resuelve por fecha, porque puede haber cambiado.
        var enRiesgo = new List<EnvioEnRiesgoResponse>();
        var pendientes = todos.Where(e => e is { EntregaPendiente: true, FechaEntregaProgramada: not null, FranjaHorariaId: not null });
        foreach (var delDia in pendientes.GroupBy(e => e.FechaEntregaProgramada!.Value))
        {
            var franjas = (await administracion.ResolverFranjasAsync(
                delDia.Select(e => e.FranjaHorariaId!.Value).Distinct().ToArray(), delDia.Key, ct)).ToDictionary(f => f.ReferenciaId);

            foreach (var envio in delDia)
            {
                if (franjas.GetValueOrDefault(envio.FranjaHorariaId!.Value) is not { } franja)
                {
                    continue;
                }

                var riesgo = RiesgoDeEntrega.Evaluar(delDia.Key, franja.Hasta, zona, ahora);
                if (riesgo.EnRiesgo)
                {
                    enRiesgo.Add(new EnvioEnRiesgoResponse(envio.Numero, envio.Estado, riesgo.FinVentana,
                        (int)Math.Floor(riesgo.Restante.TotalMinutes), riesgo.Vencida));
                }
            }
        }

        var filasConflictos = conflictos.Select(c => new ConflictoTableroResponse(c.OperacionId,
                nombres?.Repartidores.GetValueOrDefault(c.RepartidorId) ?? "Sin nombre", c.Tipo, c.Resultado,
                c.EnvioId is Guid envioId ? porId.GetValueOrDefault(envioId)?.Numero : null, c.Motivo, c.OcurridoEn))
            .ToList();

        return new TableroResponse(ahora, filas, enRiesgo.OrderBy(e => e.FinVentana).ToList(), filasConflictos);
    }
}
