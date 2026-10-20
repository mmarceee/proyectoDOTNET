using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Ejecucion;

// CU-55 en el agregado Rendicion: cómo queda cada línea al confirmar lo que volvió al depósito.
public class RendicionTests
{
    private static readonly DateTimeOffset Regreso = new(2026, 10, 22, 19, 0, 0, TimeSpan.Zero);

    private static readonly Guid NoIntentado = Guid.NewGuid();
    private static readonly Guid FallidoQueNoVuelve = Guid.NewGuid();
    private static readonly Guid Entregado = Guid.NewGuid();
    private static readonly Guid NoCargado = Guid.NewGuid();

    // Un bulto no intentado que vuelve, uno de un intento fallido que el repartidor dice que no vuelve, uno entregado
    // en la calle y uno que nunca se cargó.
    private static Rendicion Iniciada() => Rendicion.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Regreso.AddHours(-1), null,
        [
            new(Guid.NewGuid(), NoIntentado, "NoIntentado", Declarada: true, null),
            new(Guid.NewGuid(), FallidoQueNoVuelve, "IntentoFallido", Declarada: false, "Quedó en el vehículo de otro"),
            new(Guid.NewGuid(), Entregado, "EntregadoEnCalle", Declarada: false, null),
            new(Guid.NewGuid(), NoCargado, "NoCargado", Declarada: false, null),
        ]);

    private static LineaRendicion Linea(Rendicion rendicion, Guid bulto) => rendicion.Lineas.Single(l => l.BultoId == bulto);

    [Fact]
    public void Al_confirmar_cada_linea_queda_resuelta_y_los_faltantes_sin_estado_final()
    {
        var rendicion = Iniciada();

        rendicion.Confirmar(new HashSet<Guid> { NoIntentado }, Regreso);

        Assert.Equal(Regreso, rendicion.FinalizadaEn);

        Assert.True(Linea(rendicion, NoIntentado).Recibida);
        Assert.Equal(EstadoLineaRendicion.DevueltoADeposito, Linea(rendicion, NoIntentado).EstadoFinal);

        // Faltante: no se marca Extraviado solo; lo decide el despachador (CU-21).
        Assert.False(Linea(rendicion, FallidoQueNoVuelve).Recibida);
        Assert.Null(Linea(rendicion, FallidoQueNoVuelve).EstadoFinal);

        Assert.Equal(EstadoLineaRendicion.EntregadoEnCalle, Linea(rendicion, Entregado).EstadoFinal);

        // Nunca salió: no se compara.
        Assert.Null(Linea(rendicion, NoCargado).Recibida);
        Assert.Null(Linea(rendicion, NoCargado).EstadoFinal);
    }

    [Fact]
    public void Un_bulto_que_no_tenia_que_volver_es_un_sobrante_y_no_se_cambia_nada()
    {
        var rendicion = Iniciada();

        Assert.Throws<DomainException>(() => rendicion.Confirmar(new HashSet<Guid> { NoIntentado, Entregado }, Regreso));

        Assert.Null(rendicion.FinalizadaEn);
        Assert.All(rendicion.Lineas, l => Assert.Null(l.Recibida));
    }

    [Fact]
    public void Una_rendicion_confirmada_no_se_vuelve_a_confirmar()
    {
        var rendicion = Iniciada();
        rendicion.Confirmar(new HashSet<Guid> { NoIntentado }, Regreso);

        Assert.Throws<DomainException>(() => rendicion.Confirmar(new HashSet<Guid> { NoIntentado }, Regreso.AddHours(1)));
        Assert.Equal(Regreso, rendicion.FinalizadaEn);
    }
}
