using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Deposito;

public class RecepcionDepositoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    // Lo que declaró el comercio: 2 kg, 30 x 20 x 10 cm.
    private static readonly Medidas Declaradas = new(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10);

    private static readonly Medidas SinMedir = new(null, null, null, null);

    private static RecepcionDeposito Registrar(Medidas medidas) =>
        RecepcionDeposito.Registrar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Declaradas, medidas, Ahora);

    [Fact]
    public void Un_bulto_sin_medir_se_recibe_conforme()
    {
        var recepcion = Registrar(SinMedir);

        Assert.Equal(ResultadoRecepcion.Conforme, recepcion.Resultado);
        Assert.Null(recepcion.Discrepancia);
        Assert.Equal(Ahora, recepcion.RecibidoEn);
    }

    [Fact]
    public void Una_diferencia_dentro_de_la_tolerancia_es_conforme()
    {
        // 2,08 kg contra 2 kg declarados: 4 %, por debajo del 5 % de tolerancia.
        var recepcion = Registrar(SinMedir with { PesoKg = 2.08m });

        Assert.Equal(ResultadoRecepcion.Conforme, recepcion.Resultado);
    }

    [Fact]
    public void Una_diferencia_fuera_de_la_tolerancia_se_registra_como_discrepancia_sin_bloquear()
    {
        var recepcion = Registrar(SinMedir with { PesoKg = 3, AltoCm = 10 });

        Assert.Equal(ResultadoRecepcion.ConDiscrepancia, recepcion.Resultado);
        Assert.Contains("peso", recepcion.Discrepancia);
        Assert.DoesNotContain("alto", recepcion.Discrepancia);
    }

    [Fact]
    public void Una_medida_en_cero_no_es_valida()
    {
        Assert.Throws<DomainException>(() => Registrar(SinMedir with { LargoCm = 0 }));
    }
}
