using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Ejecucion;

// CU-66 (ADR-0004, sección 2.5): el despachador revisa un conflicto; sólo las operaciones rechazadas se revisan.
public class RevisarConflictoTests
{
    private static DatosOperacion Datos() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "RegistrarEntrega",
        Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, false);

    [Fact]
    public void Un_conflicto_queda_revisado_y_revisarlo_otra_vez_no_cambia_nada()
    {
        var operacion = OperacionDispositivo.Rechazada(Guid.NewGuid(), Datos(), ResultadoOperacion.Conflicto,
            "EnvioExtraviado", null, "{}");

        operacion.MarcarRevisada();
        operacion.MarcarRevisada();

        Assert.True(operacion.Revisada);
    }

    [Fact]
    public void Una_operacion_aplicada_no_se_revisa()
    {
        var operacion = OperacionDispositivo.Aplicada(Guid.NewGuid(), Datos());

        Assert.Throws<DomainException>(operacion.MarcarRevisada);
        Assert.False(operacion.Revisada);
    }
}
