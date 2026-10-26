using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// CU-20: el despachador o el comercio deciden devolver un envío al comercio (T12 o T14).
public class IniciarDevolucionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    // Lleva un envío nuevo hasta el estado pedido por el camino real de la tabla de transiciones.
    private static Envio EnvioEn(string estado)
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, Ahora.AddDays(-2));

        if (estado == "Admitido")
        {
            return envio;
        }

        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, Ahora.AddDays(-1));   // T2
        if (estado == "EnDeposito")
        {
            return envio;
        }

        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, Ahora.AddHours(-5)); // T3
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, Ahora.AddHours(-4)); // T5
        if (estado == "EnTransito")
        {
            return envio;
        }

        // Reprogramado: el intento fallido de CU-18 (T7 y T9).
        var ausente = new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
        var reglas = new ReglasEntrega(false, false, maxIntentos: 3, TimeSpan.FromHours(24));
        envio.RegistrarIntentoFallido(ausente, null, null, new Ubicacion(-34.9m, -56.16m), reglas, null, Ahora.AddHours(-3));
        return envio;
    }

    // T12 desde el Backoffice o el Portal, y T14 desde el Backoffice.
    [Theory]
    [InlineData("EnDeposito", "Backoffice")]
    [InlineData("EnDeposito", "PortalComercio")]
    [InlineData("Reprogramado", "Backoffice")]
    public void Desde_un_estado_permitido_queda_en_devolucion_con_su_evento(string estado, string origen)
    {
        var envio = EnvioEn(estado);
        var responsable = Guid.NewGuid();

        envio.IniciarDevolucion("  El cliente canceló la compra  ", Enum.Parse<OrigenEvento>(origen), responsable, Ahora);

        Assert.Equal(EstadoEnvio.EnDevolucion, envio.Estado);
        var evento = envio.Eventos[^1];
        Assert.Equal(Enum.Parse<EstadoEnvio>(estado), evento.EstadoAnterior);
        Assert.Equal(Enum.Parse<OrigenEvento>(origen), evento.Origen);
        Assert.Equal(responsable, evento.ResponsableId);
        Assert.Equal("El cliente canceló la compra", evento.Detalle);
    }

    // Paso 2: sin motivo no se inicia, y el envío no cambia.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_motivo_se_rechaza_y_no_cambia_nada(string motivo)
    {
        var envio = EnvioEn("EnDeposito");
        var eventos = envio.Eventos.Count;

        Assert.Throws<DomainException>(() => envio.IniciarDevolucion(motivo, OrigenEvento.Backoffice, null, Ahora));

        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
        Assert.Equal(eventos, envio.Eventos.Count);
    }

    // A1: un envío en la calle no se puede devolver.
    [Fact]
    public void Desde_un_estado_no_permitido_se_rechaza()
    {
        var envio = EnvioEn("EnTransito");

        Assert.Throws<TransicionInvalidaException>(() =>
            envio.IniciarDevolucion("No lo quieren", OrigenEvento.Backoffice, null, Ahora));

        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
    }

    // A2: el comercio sólo puede pedirla con el envío en el depósito, aunque la tabla permita T14 al despachador.
    [Theory]
    [InlineData("PortalComercio")]
    [InlineData("Api")]
    public void El_comercio_no_la_pide_fuera_del_deposito(string origen)
    {
        var envio = EnvioEn("Reprogramado");

        var error = Assert.Throws<DomainException>(() =>
            envio.IniciarDevolucion("Ya no lo quiero", Enum.Parse<OrigenEvento>(origen), null, Ahora));

        Assert.IsNotType<TransicionInvalidaException>(error);
        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
    }
}
