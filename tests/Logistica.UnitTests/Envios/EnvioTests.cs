using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

public class EnvioTests
{
    private static readonly Guid OperadorId = Guid.NewGuid();
    private static readonly Guid ComercioId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly DatosBulto UnBulto = new(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10, MontoTarifa: 150);

    private static Envio CrearEnvio(params DatosBulto[] bultos) =>
        Envio.Crear(
            OperadorId,
            ComercioId,
            "ENV-1",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            bultos,
            OrigenEvento.PortalComercio,
            responsableId: null,
            Ahora);

    [Fact]
    public void Un_envio_nuevo_queda_Admitido_con_su_evento_inicial()
    {
        var envio = CrearEnvio(UnBulto);

        Assert.Equal(EstadoEnvio.Admitido, envio.Estado);
        var evento = Assert.Single(envio.Eventos);
        Assert.Null(evento.EstadoAnterior);
        Assert.Equal(EstadoEnvio.Admitido, evento.EstadoNuevo);
        Assert.Equal(Ahora, evento.OcurridoEn);
    }

    [Fact]
    public void Un_envio_sin_bultos_no_se_puede_crear()
    {
        Assert.Throws<DomainException>(() => CrearEnvio());
    }

    [Fact]
    public void El_monto_del_envio_es_la_suma_de_sus_bultos()
    {
        var envio = CrearEnvio(UnBulto, UnBulto with { MontoTarifa = 100 });

        Assert.Equal(250m, envio.MontoTarifa);
        Assert.Equal(["ENV-1-1", "ENV-1-2"], envio.Bultos.Select(b => b.Codigo));
    }

    [Fact]
    public void Los_bultos_y_el_evento_heredan_el_inquilino_del_envio()
    {
        var envio = CrearEnvio(UnBulto);

        Assert.Equal(OperadorId, envio.Bultos[0].OperadorId);
        Assert.Equal(ComercioId, envio.Bultos[0].ComercioId);
        Assert.Equal(OperadorId, envio.Eventos[0].OperadorId);
        Assert.Equal(ComercioId, envio.Eventos[0].ComercioId);
    }

    [Fact]
    public void Un_bulto_sin_peso_no_se_puede_crear()
    {
        Assert.Throws<DomainException>(() => CrearEnvio(UnBulto with { PesoKg = 0 }));
    }

    [Fact]
    public void Un_destinatario_sin_nombre_no_es_valido()
    {
        Assert.Throws<DomainException>(() => new Destinatario(" ", "099123456"));
    }

    [Fact]
    public void Recibir_en_deposito_pasa_a_EnDeposito_y_agrega_el_evento_T2()
    {
        var envio = CrearEnvio(UnBulto);
        var despues = Ahora.AddHours(2);

        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, responsableId: null, despues);

        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
        Assert.Equal(2, envio.Eventos.Count);
        var evento = envio.Eventos[1];
        Assert.Equal(EstadoEnvio.Admitido, evento.EstadoAnterior);
        Assert.Equal(EstadoEnvio.EnDeposito, evento.EstadoNuevo);
        Assert.Equal(OrigenEvento.Backoffice, evento.Origen);
        Assert.Equal(despues, evento.OcurridoEn);
    }

    // Ejemplos que la tabla de transiciones exige rechazar. El estado va como texto porque
    // EstadoEnvio es internal y una prueba pública no puede recibirlo como parámetro.
    [Theory]
    [InlineData("Entregado")]
    [InlineData("EnTransito")]
    [InlineData("Admitido")]
    public void Una_transicion_que_no_esta_en_la_tabla_se_rechaza_sin_cambiar_nada(string destino)
    {
        var envio = CrearEnvio(UnBulto);

        Assert.Throws<TransicionInvalidaException>(() =>
            envio.Transicionar(Enum.Parse<EstadoEnvio>(destino), OrigenEvento.Backoffice, responsableId: null, Ahora));

        Assert.Equal(EstadoEnvio.Admitido, envio.Estado);
        Assert.Single(envio.Eventos);
    }

    [Fact]
    public void Un_envio_recibido_en_deposito_ya_no_se_puede_cancelar()
    {
        var envio = CrearEnvio(UnBulto);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, responsableId: null, Ahora);

        Assert.Throws<TransicionInvalidaException>(() =>
            envio.Transicionar(EstadoEnvio.Cancelado, OrigenEvento.PortalComercio, responsableId: null, Ahora));
    }
}
