using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// CU-19: el despachador elige la fecha y la franja del nuevo intento de un envío reprogramado.
public class ReprogramarEnvioTests
{
    private static readonly Guid Zona = Guid.NewGuid();
    private static readonly Guid Franja = Guid.NewGuid();
    private static readonly DateOnly Manana = new(2026, 10, 23);

    // Falla el 22/10 a las 11:30 de Montevideo (14:30 UTC) con 24 h de plazo: se reintenta desde el 23/10, 14:30 UTC.
    private static readonly DateTimeOffset LimiteDelPlazo = new(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);

    // El camino real hasta Reprogramado: T2, T3, T5 y el intento fallido de CU-18 (T7 y T9).
    private static Envio Reprogramado()
    {
        var salida = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, salida.AddDays(-1));
        envio.DefinirPlanificacion(Zona, null, null);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, salida.AddHours(-12));
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, salida.AddHours(-1));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, salida);

        var ausente = new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
        var reglas = new ReglasEntrega(false, false, maxIntentos: 3, TimeSpan.FromHours(24));
        envio.RegistrarIntentoFallido(ausente, null, null, new Ubicacion(-34.9m, -56.16m), reglas, null,
            new DateTimeOffset(2026, 10, 22, 11, 30, 0, TimeSpan.FromHours(-3)));

        return envio;
    }

    [Fact]
    public void Una_franja_despues_del_plazo_queda_como_nuevo_intento()
    {
        var envio = Reprogramado();

        // La franja de 14 a 18 de Montevideo empieza a las 17:00 UTC: después del límite.
        envio.ElegirNuevoIntento(Manana, Franja, new DateTimeOffset(2026, 10, 23, 17, 0, 0, TimeSpan.Zero));

        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
        Assert.Equal(Manana, envio.FechaEntregaProgramada);
        Assert.Equal(Franja, envio.FranjaHorariaId);
    }

    // A1: la franja de 9 a 13 de Montevideo empieza a las 12:00 UTC, antes del límite.
    [Fact]
    public void Una_franja_que_empieza_antes_del_plazo_se_rechaza_y_no_cambia_nada()
    {
        var envio = Reprogramado();

        Assert.Throws<DomainException>(() =>
            envio.ElegirNuevoIntento(Manana, Franja, new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)));

        Assert.Null(envio.FranjaHorariaId);
    }

    // Acordado con Lucas y Ezequiel: una franja que empieza justo en el límite se admite.
    [Fact]
    public void Una_franja_que_empieza_justo_en_el_limite_se_admite()
    {
        var envio = Reprogramado();

        envio.ElegirNuevoIntento(Manana, Franja, LimiteDelPlazo);

        Assert.Equal(Franja, envio.FranjaHorariaId);
    }

    [Fact]
    public void Solo_se_elige_un_nuevo_intento_para_un_envio_reprogramado()
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-2",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, LimiteDelPlazo);

        Assert.Throws<DomainException>(() => envio.ElegirNuevoIntento(Manana, Franja, LimiteDelPlazo.AddDays(1)));
    }
}
