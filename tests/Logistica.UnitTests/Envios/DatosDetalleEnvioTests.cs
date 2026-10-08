using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

public sealed class DatosDetalleEnvioTests
{
    [Fact]
    public void Un_intento_exitoso_necesita_firma()
    {
        var envio = CrearEnvio();
        var prueba = new PruebaEntrega(null, Guid.NewGuid(), null, null,
            new Ubicacion(-34, -56), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => envio.AgregarIntento(1, DateTimeOffset.UtcNow,
            ResultadoIntento.Exitoso, null, prueba));
    }

    [Fact]
    public void Un_intento_fallido_necesita_motivo()
    {
        Assert.Throws<DomainException>(() => CrearEnvio().AgregarIntento(1, DateTimeOffset.UtcNow,
            ResultadoIntento.Fallido, null, null));
    }

    [Fact]
    public void No_se_repite_el_numero_de_intento_y_se_copia_el_inquilino_del_envio()
    {
        var envio = CrearEnvio();
        var intento = envio.AgregarIntento(1, DateTimeOffset.UtcNow, ResultadoIntento.Fallido, Guid.NewGuid(), null);

        Assert.Equal(envio.OperadorId, intento.OperadorId);
        Assert.Equal(envio.ComercioId, intento.ComercioId);
        Assert.Equal(envio.Id, intento.EnvioId);
        Assert.Throws<DomainException>(() => envio.AgregarIntento(1, DateTimeOffset.UtcNow,
            ResultadoIntento.Fallido, Guid.NewGuid(), null));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public void Las_coordenadas_fuera_de_rango_se_rechazan(int latitud, int longitud)
    {
        Assert.Throws<DomainException>(() => new Ubicacion(latitud, longitud));
    }

    [Fact]
    public void Los_archivos_de_evidencia_respetan_el_limite_y_el_tipo()
    {
        var envio = CrearEnvio();
        Assert.Throws<DomainException>(() => ArchivoEvidencia.Crear(envio, "image/png", new byte[ArchivoEvidencia.MaximoBytes + 1]));
        Assert.Throws<DomainException>(() => ArchivoEvidencia.Crear(envio, "text/html", [1]));
        Assert.Throws<DomainException>(() => ArchivoEvidencia.Crear(envio, "image/png", []));
    }

    [Fact]
    public void No_se_admite_una_version_tarifaria_sin_identificador_valido()
    {
        Assert.Throws<DomainException>(() => CrearEnvio(new DatosVersionTarifario(Guid.Empty, 1)));
        Assert.Throws<DomainException>(() => CrearEnvio(new DatosVersionTarifario(Guid.NewGuid(), 0)));
    }

    private static Envio CrearEnvio(DatosVersionTarifario? version = null) => Envio.Crear(
        Guid.NewGuid(), Guid.NewGuid(), "ENV-PRUEBA", new Destinatario("Ana", "099123456"),
        new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
        [new DatosBulto(1, 10, 10, 10, 100)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow, version);
}
