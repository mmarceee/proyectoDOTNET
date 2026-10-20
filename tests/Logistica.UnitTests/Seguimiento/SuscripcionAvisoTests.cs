using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Seguimiento;

public class SuscripcionAvisoTests
{
    private static SuscripcionAviso Nueva(string url = "https://comercio.example/avisos",
        IReadOnlyCollection<TipoEventoAviso>? tipos = null, string secreto = "secreto-cifrado") =>
        SuscripcionAviso.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), url,
            tipos ?? [TipoEventoAviso.CambioDeEstado], secreto);

    [Fact]
    public void Crear_conserva_el_vinculo_comercial_y_nace_activa()
    {
        var operadorId = Guid.NewGuid();
        var comercioId = Guid.NewGuid();
        var relacionId = Guid.NewGuid();
        var suscripcion = SuscripcionAviso.Crear(operadorId, comercioId, relacionId,
            "https://comercio.example/avisos", [TipoEventoAviso.EntregaFallida, TipoEventoAviso.Devolucion],
            "secreto-cifrado");

        Assert.NotEqual(Guid.Empty, suscripcion.Id);
        Assert.Equal(operadorId, suscripcion.OperadorId);
        Assert.Equal(comercioId, suscripcion.ComercioId);
        Assert.Equal(relacionId, suscripcion.RelacionComercialId);
        Assert.True(suscripcion.Activa);
        Assert.Equal("https://comercio.example/avisos", suscripcion.UrlDestino);
        Assert.Equal("secreto-cifrado", suscripcion.SecretoFirma);
        Assert.Equal([TipoEventoAviso.EntregaFallida, TipoEventoAviso.Devolucion], suscripcion.TiposEvento);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Rechaza_identificadores_vacios(int indice)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        ids[indice] = Guid.Empty;
        Assert.Throws<DomainException>(() => SuscripcionAviso.Crear(ids[0], ids[1], ids[2],
            "https://comercio.example/avisos", [TipoEventoAviso.CambioDeEstado], "secreto-cifrado"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-url")]
    [InlineData("/avisos")]
    [InlineData("http://comercio.example/avisos")]
    [InlineData("ftp://comercio.example/avisos")]
    [InlineData("https://")]
    public void Rechaza_direcciones_que_no_son_https(string url) =>
        Assert.Throws<DomainException>(() => Nueva(url));

    [Fact]
    public void Rechaza_eventos_vacios_o_desconocidos()
    {
        Assert.Throws<DomainException>(() => Nueva(tipos: []));
        Assert.Throws<DomainException>(() => Nueva(tipos: [TipoEventoAviso.CambioDeEstado, (TipoEventoAviso)99]));
        Assert.Throws<DomainException>(() => SuscripcionAviso.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "https://comercio.example/avisos", null!, "secreto-cifrado"));
    }

    [Fact]
    public void Los_eventos_no_se_duplican_ni_pueden_modificarse_desde_afuera()
    {
        var tipos = new List<TipoEventoAviso> { TipoEventoAviso.CambioDeEstado, TipoEventoAviso.CambioDeEstado };
        var suscripcion = Nueva(tipos: tipos);
        tipos.Clear();

        Assert.Equal(TipoEventoAviso.CambioDeEstado, Assert.Single(suscripcion.TiposEvento));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<TipoEventoAviso>)suscripcion.TiposEvento).Add(TipoEventoAviso.Devolucion));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Regenerar_reemplaza_el_secreto_sin_cambiar_la_activacion(bool desactivar)
    {
        var suscripcion = Nueva();
        if (desactivar) suscripcion.Desactivar();

        suscripcion.RegenerarSecreto("nuevo-secreto-cifrado");

        Assert.Equal("nuevo-secreto-cifrado", suscripcion.SecretoFirma);
        Assert.Equal(!desactivar, suscripcion.Activa);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Un_secreto_invalido_no_se_acepta_ni_reemplaza_el_anterior(string? secreto)
    {
        Assert.Throws<DomainException>(() => Nueva(secreto: secreto!));
        var suscripcion = Nueva();
        Assert.Throws<DomainException>(() => suscripcion.RegenerarSecreto(secreto!));
        Assert.Equal("secreto-cifrado", suscripcion.SecretoFirma);
        Assert.True(suscripcion.Activa);
    }

    [Fact]
    public void Desactivar_conserva_la_configuracion()
    {
        var suscripcion = Nueva();
        suscripcion.Desactivar();
        Assert.False(suscripcion.Activa);
        Assert.Equal("https://comercio.example/avisos", suscripcion.UrlDestino);
        Assert.Equal("secreto-cifrado", suscripcion.SecretoFirma);
        Assert.Equal(TipoEventoAviso.CambioDeEstado, Assert.Single(suscripcion.TiposEvento));
    }
}
