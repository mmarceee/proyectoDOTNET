using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// CU-21 (T16): el administrador o el despachador declara extraviado un envío.
public class DeclararExtravioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

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

        if (estado == "EnTransito")
        {
            envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, Ahora.AddHours(-3)); // T3
            envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, Ahora.AddHours(-2)); // T5
        }
        else if (estado == "EnDevolucion")
        {
            envio.Transicionar(EstadoEnvio.EnDevolucion, OrigenEvento.Backoffice, null, Ahora.AddHours(-2));  // T12
        }

        return envio;
    }

    // Los tres estados de origen de T16.
    [Theory]
    [InlineData("EnDeposito")]
    [InlineData("EnTransito")]
    [InlineData("EnDevolucion")]
    public void Desde_un_estado_permitido_queda_extraviado_con_su_evento(string estado)
    {
        var envio = EnvioEn(estado);
        var responsable = Guid.NewGuid();

        envio.DeclararExtravio("  Robo de la camioneta  ", responsable, Ahora);

        Assert.Equal(EstadoEnvio.Extraviado, envio.Estado);
        var evento = envio.Eventos[^1];
        Assert.Equal(Enum.Parse<EstadoEnvio>(estado), evento.EstadoAnterior);
        Assert.Equal(EstadoEnvio.Extraviado, evento.EstadoNuevo);
        Assert.Equal(OrigenEvento.Backoffice, evento.Origen);
        Assert.Equal(responsable, evento.ResponsableId);
        Assert.Equal("Robo de la camioneta", evento.Detalle);
    }

    // A1: el motivo es obligatorio, y si falta el envío no cambia.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_motivo_se_rechaza_y_no_cambia_nada(string motivo)
    {
        var envio = EnvioEn("EnDeposito");
        var eventos = envio.Eventos.Count;

        Assert.Throws<DomainException>(() => envio.DeclararExtravio(motivo, null, Ahora));

        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
        Assert.Equal(eventos, envio.Eventos.Count);
    }

    // A2: un envío que todavía no llegó al depósito no se puede declarar extraviado.
    [Fact]
    public void Desde_un_estado_no_permitido_se_rechaza()
    {
        var envio = EnvioEn("Admitido");

        Assert.Throws<TransicionInvalidaException>(() => envio.DeclararExtravio("No aparece", null, Ahora));

        Assert.Equal(EstadoEnvio.Admitido, envio.Estado);
    }
}
