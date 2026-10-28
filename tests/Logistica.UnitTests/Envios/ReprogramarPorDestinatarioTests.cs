using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// CU-61: el destinatario elige desde el seguimiento público una nueva fecha y franja (T18, T19 o un nuevo intento).
public class ReprogramarPorDestinatarioTests
{
    private static readonly Guid Zona = Guid.NewGuid();
    private static readonly Guid Franja = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 10, 29);
    private static readonly DateTimeOffset Comienzo = new(2026, 10, 29, 17, 0, 0, TimeSpan.Zero); // 14:00 en Montevideo
    private static readonly TimeSpan Anticipacion = TimeSpan.FromHours(24);

    // Lleva un envío nuevo hasta el estado pedido por el camino real de la tabla de transiciones.
    private static Envio EnvioEn(string estado)
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, Ahora.AddDays(-2));
        envio.DefinirPlanificacion(Zona, null, null);
        if (estado == "Admitido") return envio;

        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, Ahora.AddDays(-1));      // T2
        if (estado == "EnDeposito") return envio;

        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, Ahora.AddHours(-6));  // T3
        if (estado == "AsignadoARuta") return envio;

        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, Ahora.AddHours(-5));  // T5
        if (estado == "EnTransito") return envio;

        // Reprogramado: un intento fallido de CU-18 (T7 y T9), con 24 h entre intentos.
        var ausente = new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
        var reglas = new ReglasEntrega(false, false, maxIntentos: 3, TimeSpan.FromHours(24));
        envio.RegistrarIntentoFallido(ausente, null, null, new Ubicacion(-34.9m, -56.16m), reglas, null, Ahora.AddHours(-4));
        return envio;
    }

    private static void Reprogramar(Envio envio, DateTimeOffset? comienzo = null, int maxIntentos = 3) =>
        envio.ReprogramarPorDestinatario(Fecha, Franja, comienzo ?? Comienzo, Anticipacion, maxIntentos, Ahora);

    // T18 y T19: queda Reprogramado con la fecha y la franja nuevas, y el evento con origen en el seguimiento público.
    [Theory]
    [InlineData("EnDeposito")]
    [InlineData("AsignadoARuta")]
    public void Desde_deposito_o_ruta_queda_reprogramado_con_la_nueva_entrega(string estado)
    {
        var envio = EnvioEn(estado);

        Reprogramar(envio);

        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
        Assert.Equal(Fecha, envio.FechaEntregaProgramada);
        Assert.Equal(Franja, envio.FranjaHorariaId);
        var evento = envio.Eventos[^1];
        Assert.Equal(Enum.Parse<EstadoEnvio>(estado), evento.EstadoAnterior);
        Assert.Equal(OrigenEvento.SeguimientoPublico, evento.Origen);
    }

    // Admitido: sólo cambia la fecha y la franja, sin cambio de estado (post-condición, primer caso).
    [Fact]
    public void Admitido_registra_la_nueva_entrega_sin_cambiar_de_estado()
    {
        var envio = EnvioEn("Admitido");
        var eventos = envio.Eventos.Count;

        Reprogramar(envio);

        Assert.Equal(EstadoEnvio.Admitido, envio.Estado);
        Assert.Equal(Fecha, envio.FechaEntregaProgramada);
        Assert.Equal(Franja, envio.FranjaHorariaId);
        Assert.Equal(eventos, envio.Eventos.Count);
    }

    // Reprogramado después de un intento fallido: elige el nuevo intento, como el despachador en CU-19.
    [Fact]
    public void Reprogramado_elige_su_nuevo_intento()
    {
        var envio = EnvioEn("Reprogramado");

        Reprogramar(envio);

        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
        Assert.Equal(Franja, envio.FranjaHorariaId);
    }

    // A1: ya salió a reparto.
    [Fact]
    public void En_transito_ya_no_se_puede_reprogramar()
    {
        var envio = EnvioEn("EnTransito");

        Assert.Throws<DomainException>(() => Reprogramar(envio));

        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
    }

    // A2: sin la anticipación mínima no cambia nada.
    [Fact]
    public void Sin_la_anticipacion_minima_se_rechaza_y_no_cambia_nada()
    {
        var envio = EnvioEn("EnDeposito");

        Assert.Throws<DomainException>(() => Reprogramar(envio, comienzo: Ahora.AddHours(23)));

        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
        Assert.Null(envio.FranjaHorariaId);
    }

    // A2: con los intentos agotados decide el despachador.
    [Fact]
    public void Con_los_intentos_agotados_no_se_puede_reprogramar()
    {
        var envio = EnvioEn("Reprogramado");

        Assert.Throws<DomainException>(() => Reprogramar(envio, maxIntentos: 1));

        Assert.Null(envio.FranjaHorariaId);
    }
}
