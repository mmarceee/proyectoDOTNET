using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Ejecucion.Application.Features.SincronizarOperaciones;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Envios.Contracts.Results;

namespace Logistica.UnitTests.Ejecucion;

// ADR-0004, sección 2.11: cada bulto de una visita se compara con su estado en el servidor antes de aplicarla.
public class DiagnosticoVisitaTests
{
    private static readonly Guid Ausente = Guid.NewGuid();

    private static EnvioParaVisita Envio(bool admiteParcial, params string[] estados) =>
        new(Guid.NewGuid(), estados.Select((e, i) => new BultoParaVisita(Guid.NewGuid(), $"B-{i + 1}", e)).ToList(), admiteParcial);

    private static ResultadoBultoRequest Entregado(string codigo) => new(codigo);
    private static ResultadoBultoRequest NoEntregado(string codigo) => new(codigo, Ausente);

    [Fact]
    public void Una_visita_con_todos_los_bultos_en_transito_no_tiene_rechazo()
    {
        var envio = Envio(admiteParcial: false, "EnTransito", "EnTransito");

        Assert.Equal(Diagnostico.SinRechazo, DiagnosticoVisita.Evaluar([Entregado("B-1"), Entregado("b-2 ")], envio));
    }

    // Prueba 10: un bulto extraviado rechaza la visita entera, y el diagnóstico dice cuál.
    [Fact]
    public void Un_bulto_extraviado_es_un_conflicto_de_toda_la_visita()
    {
        var envio = Envio(admiteParcial: false, "EnTransito", "Extraviado", "EnTransito");

        var diagnostico = DiagnosticoVisita.Evaluar([Entregado("B-1"), Entregado("B-2"), Entregado("B-3")], envio);

        Assert.Equal((ResultadoOperacion.Conflicto, "BultoExtraviado"), (diagnostico.Rechazo, diagnostico.Motivo));
        Assert.Equal([new BultoRechazadoResponse("B-2", "BultoExtraviado")], diagnostico.Bultos);
    }

    [Theory]
    [InlineData("Extraviado", "BultoExtraviado")]
    [InlineData("EnDeposito", "BultoEnDeposito")]
    [InlineData("Entregado", "BultoYaResuelto")]
    [InlineData("Reprogramado", "BultoYaResuelto")]
    [InlineData("EnDevolucion", "BultoYaResuelto")]
    [InlineData("Devuelto", "BultoYaResuelto")]
    [InlineData("AsignadoARuta", "BultoFueraDeRuta")]
    public void El_estado_del_bulto_en_el_servidor_dice_el_motivo_del_conflicto(string estado, string motivo)
    {
        var diagnostico = DiagnosticoVisita.Evaluar([Entregado("B-1")], Envio(admiteParcial: true, estado));

        Assert.Equal((ResultadoOperacion.Conflicto, motivo), (diagnostico.Rechazo, diagnostico.Motivo));
    }

    // Prueba 12: se evalúan todos los bultos, y un dato mal armado prevalece sobre un conflicto.
    [Fact]
    public void Un_bulto_ajeno_repetido_o_sin_resultado_vuelve_invalida_la_visita_con_el_motivo_de_cada_uno()
    {
        var envio = Envio(admiteParcial: true, "EnTransito", "EnTransito", "Extraviado");

        var diagnostico = DiagnosticoVisita.Evaluar([Entregado("B-1"), Entregado("B-1"), Entregado("OTRO"), Entregado("B-3")],
            envio);

        Assert.Equal((ResultadoOperacion.Invalida, "BultoRepetido"), (diagnostico.Rechazo, diagnostico.Motivo));
        Assert.Equal(
            [
                new BultoRechazadoResponse("B-1", "BultoRepetido"),
                new BultoRechazadoResponse("OTRO", "BultoAjenoAlEnvio"),
                new BultoRechazadoResponse("B-2", "BultoSinResultado"),
                new BultoRechazadoResponse("B-3", "BultoExtraviado"),
            ],
            diagnostico.Bultos);
    }

    // Prueba 11: RF 33, sin entrega parcial se entregan todos los bultos o ninguno.
    [Theory]
    [InlineData(false, "EntregaParcialNoAdmitida")]
    [InlineData(true, null)]
    public void Una_entrega_de_parte_de_los_bultos_depende_de_las_reglas_del_envio(bool admiteParcial, string? motivo)
    {
        var envio = Envio(admiteParcial, "EnTransito", "EnTransito");

        var diagnostico = DiagnosticoVisita.Evaluar([Entregado("B-1"), NoEntregado("B-2")], envio);

        Assert.Equal(motivo is null ? null : ResultadoOperacion.Invalida, diagnostico.Rechazo);
        Assert.Equal(motivo, diagnostico.Motivo);
    }

    [Fact]
    public void Una_visita_sin_entregas_no_es_una_entrega_parcial()
    {
        var envio = Envio(admiteParcial: false, "EnTransito", "EnTransito");

        Assert.Null(DiagnosticoVisita.Evaluar([NoEntregado("B-1"), NoEntregado("B-2")], envio).Rechazo);
    }
}
