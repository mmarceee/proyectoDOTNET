using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Envios.Contracts.Results;

namespace Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;

// El resultado de clasificar una visita antes de aplicarla. Sin rechazo, la visita se puede registrar; con rechazo, no se
// aplica ningún bulto y Bultos dice el motivo de cada bulto rechazado (ADR-0004, sección 2.11).
internal sealed record Diagnostico(ResultadoOperacion? Rechazo, string? Motivo, IReadOnlyList<BultoRechazadoResponse> Bultos)
{
    public static readonly Diagnostico SinRechazo = new(null, null, []);
}

// ADR-0004, sección 2.11: cada bulto de la visita se compara con su estado actual en el servidor. Se evalúan todos antes
// de decidir, así el diagnóstico queda completo y no sólo con el primer error. Un dato mal armado (un bulto ajeno,
// repetido o sin resultado) vuelve inválida la visita; un estado que cambió en el servidor, en conflicto.
internal static class DiagnosticoVisita
{
    private const string EnTransito = "EnTransito";

    public static Diagnostico Evaluar(IReadOnlyList<ResultadoBultoRequest> resultados, EnvioParaVisita envio)
    {
        var porCodigo = envio.Bultos.ToDictionary(b => b.Codigo, StringComparer.OrdinalIgnoreCase);
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalidos = new List<BultoRechazadoResponse>();
        var conflictos = new List<BultoRechazadoResponse>();

        foreach (var resultado in resultados)
        {
            var codigo = resultado.CodigoBulto?.Trim() ?? "";
            if (!vistos.Add(codigo))
            {
                invalidos.Add(new(codigo, "BultoRepetido"));
            }
            else if (!porCodigo.TryGetValue(codigo, out var bulto))
            {
                invalidos.Add(new(codigo, "BultoAjenoAlEnvio"));
            }
            else if (MotivoDeConflicto(bulto.Estado) is { } motivo)
            {
                conflictos.Add(new(bulto.Codigo, motivo));
            }
        }

        // Cada bulto del envío que viaja en la ruta tiene que tener su resultado.
        invalidos.AddRange(envio.Bultos.Where(b => b.Estado == EnTransito && !vistos.Contains(b.Codigo))
            .Select(b => new BultoRechazadoResponse(b.Codigo, "BultoSinResultado")));

        if (invalidos.Count > 0)
        {
            return new(ResultadoOperacion.Invalida, invalidos[0].Motivo, [.. invalidos, .. conflictos]);
        }

        if (conflictos.Count > 0)
        {
            return new(ResultadoOperacion.Conflicto, conflictos[0].Motivo, conflictos);
        }

        // RF 33: sin entrega parcial, se entregan todos los bultos o ninguno. La PWA lo impide con las reglas descargadas.
        var entregados = resultados.Count(r => r.MotivoId is null);
        if (entregados > 0 && entregados < resultados.Count && !envio.AdmiteEntregaParcial)
        {
            return new(ResultadoOperacion.Invalida, "EntregaParcialNoAdmitida", []);
        }

        return Diagnostico.SinRechazo;
    }

    // ADR-0004, secciones 1.2 y 2.11: por qué el servidor ya no puede aplicar el resultado de un bulto.
    private static string? MotivoDeConflicto(string estado)
    {
        return estado switch
        {
            EnTransito => null,
            "Extraviado" => "BultoExtraviado",
            "EnDeposito" => "BultoEnDeposito",
            "Entregado" or "NoEntregado" or "Reprogramado" or "EnDevolucion" or "Devuelto" => "BultoYaResuelto",
            // Admitido, AsignadoARuta o Cancelado: el bulto no se cargó en esta ruta.
            _ => "BultoFueraDeRuta",
        };
    }
}
