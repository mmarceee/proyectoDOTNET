using Logistica.Http.Contracts.Repartidor;

namespace Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;

// CU-52: procesa el lote en el orden en que ocurrió, una operación por vez y cada una en su transacción (ADR-0004,
// sección 2.6). Un conflicto no detiene el lote; las operaciones siguientes sobre el mismo envío se rechazan, porque
// dependían de un estado que no se alcanzó.
internal sealed class SincronizarOperacionesHandler(IProcesadorDeOperacion procesador)
{
    public async Task<SincronizacionResponse> HandleAsync(SincronizarOperacionesCommand command, CancellationToken ct)
    {
        var enviosConRechazo = new HashSet<Guid>();
        var rutasConCargaRechazada = new HashSet<Guid>();
        var resultados = new List<ResultadoOperacionResponse>();

        foreach (var operacion in command.Operaciones.OrderBy(o => o.Secuencia))
        {
            var rechazoPrevio = operacion.EnvioId is Guid envio && enviosConRechazo.Contains(envio)
                || rutasConCargaRechazada.Contains(operacion.RutaId);
            var resultado = await procesador.ProcesarAsync(
                new ProcesarOperacionCommand(command.RepartidorId, command.UsuarioId, operacion, rechazoPrevio), ct);

            if (operacion.EnvioId is Guid envioId && resultado.Resultado is Resultados.Conflicto or Resultados.Invalida)
            {
                enviosConRechazo.Add(envioId);
            }

            resultados.Add(resultado);
            if (operacion.Tipo == "ConfirmarCarga" && resultado.Resultado is Resultados.Conflicto or Resultados.Invalida)
                rutasConCargaRechazada.Add(operacion.RutaId);
        }

        return new SincronizacionResponse(resultados);
    }
}

// Los resultados que ve la PWA (ADR-0004, sección 2.4).
internal static class Resultados
{
    public const string Aplicada = "Aplicada";
    public const string YaAplicada = "YaAplicada";
    public const string Conflicto = "Conflicto";
    public const string Invalida = "Invalida";
}
