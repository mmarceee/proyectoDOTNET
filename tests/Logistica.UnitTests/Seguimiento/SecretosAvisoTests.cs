using System.Security.Cryptography;
using Logistica.Modules.Seguimiento.Infrastructure.Security;

namespace Logistica.UnitTests.Seguimiento;

public class SecretosAvisoTests
{
    private static readonly Guid Operador = Guid.NewGuid();
    private static readonly Guid Comercio = Guid.NewGuid();
    private static string Clave() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Genera_secretos_de_32_bytes_y_un_nonce_distinto_en_cada_cifrado()
    {
        var servicio = new SecretosAviso(Clave());
        var secreto = servicio.Generar();
        Assert.Equal(32, Convert.FromBase64String(secreto).Length);
        Assert.NotEqual(secreto, servicio.Generar());
        var primero = servicio.Cifrar(secreto, Operador, Comercio);
        var segundo = servicio.Cifrar(secreto, Operador, Comercio);
        Assert.NotEqual(primero, segundo);
        Assert.DoesNotContain(secreto, primero);
        Assert.Equal(secreto, servicio.Descifrar(primero, Operador, Comercio));
        Assert.Equal(secreto, servicio.Descifrar(segundo, Operador, Comercio));
    }

    [Fact]
    public void Otra_instancia_con_la_misma_clave_recupera_el_secreto()
    {
        var clave = Clave();
        var api = new SecretosAviso(clave);
        var secreto = api.Generar();
        var cifrado = api.Cifrar(secreto, Operador, Comercio);
        var worker = new SecretosAviso(clave);
        Assert.Equal(secreto, worker.Descifrar(cifrado, Operador, Comercio));
        Assert.ThrowsAny<CryptographicException>(() => new SecretosAviso(Clave()).Descifrar(cifrado, Operador, Comercio));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(44)]
    public void Rechaza_nonce_cuerpo_o_tag_alterados(int indice)
    {
        var servicio = new SecretosAviso(Clave());
        var sobre = Convert.FromBase64String(servicio.Cifrar(servicio.Generar(), Operador, Comercio)[3..]);
        sobre[indice] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() =>
            servicio.Descifrar("v1." + Convert.ToBase64String(sobre), Operador, Comercio));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_permite_trasladar_el_secreto_a_otro_operador_o_comercio(bool otroOperador)
    {
        var servicio = new SecretosAviso(Clave());
        var cifrado = servicio.Cifrar(servicio.Generar(), Operador, Comercio);
        Assert.ThrowsAny<CryptographicException>(() => servicio.Descifrar(cifrado,
            otroOperador ? Guid.NewGuid() : Operador, otroOperador ? Comercio : Guid.NewGuid()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-base64")]
    public void Una_clave_ausente_o_mal_formada_falla_sin_exponer_su_valor(string? clave)
    {
        var servicio = new SecretosAviso(clave);
        var error = Assert.Throws<InvalidOperationException>(() => servicio.Cifrar(servicio.Generar(), Operador, Comercio));
        Assert.Contains("Seguimiento:Avisos:ClaveCifrado", error.Message);
        if (!string.IsNullOrEmpty(clave)) Assert.DoesNotContain(clave, error.Message);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    public void Exige_una_clave_maestra_de_exactamente_32_bytes(int largo)
    {
        var servicio = new SecretosAviso(Convert.ToBase64String(new byte[largo]));
        Assert.Throws<InvalidOperationException>(() => servicio.Cifrar(servicio.Generar(), Operador, Comercio));
    }

    [Theory]
    [InlineData("v2.AAAA")]
    [InlineData("v1.AAAA")]
    [InlineData("")]
    public void Rechaza_versiones_o_longitudes_invalidas_del_cifrado(string cifrado) =>
        Assert.Throws<CryptographicException>(() => new SecretosAviso(Clave()).Descifrar(cifrado, Operador, Comercio));
}
