using System.Diagnostics;
using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Ejecucion.Infrastructure;

// Cada operación del lote en un ámbito de DI nuevo: DbContext, conexión y unidad de trabajo propios (ADR-0004,
// sección 2.6). El inquilino sigue siendo el de la sesión, porque se lee del request. Si otra operación cambió el
// mismo envío en ese instante (concurrencia optimista), se reevalúa una vez con un ámbito limpio.
// Cada operación es además un tramo de la traza que empezó en el dispositivo (sección 2.9; letra, sección 6.12).
internal sealed class ProcesadorEnAmbitoPropio(IServiceScopeFactory ambitos) : IProcesadorDeOperacion
{
    private static readonly ActivitySource Trazas = new(EjecucionModule.FuenteDeTrazas);

    public async Task<ResultadoOperacionResponse> ProcesarAsync(ProcesarOperacionCommand command, CancellationToken ct)
    {
        using var actividad = IniciarActividad(command.Operacion);

        ResultadoOperacionResponse resultado;
        try
        {
            resultado = await ProcesarEnAmbitoNuevoAsync(command, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            actividad?.AddEvent(new ActivityEvent("Reintento por concurrencia optimista"));
            resultado = await ProcesarEnAmbitoNuevoAsync(command, ct);
        }

        actividad?.SetTag("logistica.operacion.resultado", resultado.Resultado);
        actividad?.SetTag("logistica.operacion.motivo", resultado.Motivo);
        return resultado;
    }

    private async Task<ResultadoOperacionResponse> ProcesarEnAmbitoNuevoAsync(ProcesarOperacionCommand command, CancellationToken ct)
    {
        await using var ambito = ambitos.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ProcesarOperacionHandler>().HandleAsync(command, ct);
    }

    // La operación continúa la traza del dispositivo: su CorrelacionId es el traceparent (W3C) que generó la PWA al
    // registrarla, aunque llegue horas después. Así la entrega, su evento en el outbox (que guarda Activity.Current) y
    // más adelante el Worker quedan en la misma traza. El request de la sincronización queda como vínculo, porque un
    // lote junta operaciones de trazas distintas. Sin CorrelacionId válido, la operación cuelga del request.
    private static Activity? IniciarActividad(OperacionDispositivoRequest operacion)
    {
        var request = Activity.Current?.Context;
        var delDispositivo = ActivityContext.TryParse(operacion.CorrelacionId, null, out var padre);

        var actividad = delDispositivo
            ? Trazas.StartActivity("Sincronizar operación", ActivityKind.Internal, padre,
                links: request is ActivityContext contexto ? [new ActivityLink(contexto)] : null)
            : Trazas.StartActivity("Sincronizar operación");

        actividad?.SetTag("logistica.operacion.id", operacion.OperacionId);
        actividad?.SetTag("logistica.operacion.tipo", operacion.Tipo);
        actividad?.SetTag("logistica.envio.id", operacion.EnvioId);
        actividad?.SetTag("logistica.ruta.id", operacion.RutaId);
        return actividad;
    }
}
