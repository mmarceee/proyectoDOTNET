using System.Security.Cryptography;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;

namespace Logistica.UnitTests.Seguimiento;

// CU-60: el token del enlace público. No se puede adivinar, alterar ni falsificar sin la clave (ADR-0002, sección 2.5).
public class TokenSeguimientoTests
{
    private static readonly Guid Operador = Guid.NewGuid();
    private static readonly Guid Envio = Guid.NewGuid();

    private static TokenSeguimiento Tokens(byte[]? clave = null) =>
        new(new ClaveSeguimiento(Convert.ToBase64String(clave ?? RandomNumberGenerator.GetBytes(32))));

    [Fact]
    public void El_token_devuelve_el_operador_y_el_envio_y_siempre_es_el_mismo()
    {
        var tokens = Tokens();

        var token = tokens.Generar(Operador, Envio);

        Assert.True(tokens.TryLeer(token, out var operador, out var envio));
        Assert.Equal(Operador, operador);
        Assert.Equal(Envio, envio);
        Assert.Equal(token, tokens.Generar(Operador, Envio));
        Assert.DoesNotContain(Envio.ToString(), token);
    }

    // A1: cambiar un solo carácter lo invalida.
    [Fact]
    public void Un_token_alterado_no_sirve()
    {
        var tokens = Tokens();
        var token = tokens.Generar(Operador, Envio);
        var otraLetra = token[5] == 'A' ? 'B' : 'A';
        var alterado = token[..5] + otraLetra + token[6..];

        Assert.False(tokens.TryLeer(alterado, out _, out _));
    }

    // Otro envío u otro operador con la firma de éste: no sirve (no se puede "armar" el token de otro envío).
    [Fact]
    public void No_se_puede_cambiar_el_envio_conservando_la_firma()
    {
        var tokens = Tokens();
        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(tokens.Generar(Operador, Envio));
        Guid.NewGuid().ToByteArray().CopyTo(bytes, 0);

        Assert.False(tokens.TryLeer(System.Buffers.Text.Base64Url.EncodeToString(bytes), out _, out _));
    }

    // Sin la clave no se puede falsificar: otra clave da otro token.
    [Fact]
    public void Un_token_firmado_con_otra_clave_no_sirve()
    {
        var token = Tokens().Generar(Operador, Envio);

        Assert.False(Tokens().TryLeer(token, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("no-es-un-token!!")]
    public void Lo_que_no_es_un_token_no_sirve(string? token)
    {
        Assert.False(Tokens().TryLeer(token, out _, out _));
    }

    [Fact]
    public void Sin_una_clave_de_32_bytes_no_se_firma()
    {
        Assert.Throws<InvalidOperationException>(() => new ClaveSeguimiento(""));
        Assert.Throws<InvalidOperationException>(() => new ClaveSeguimiento(Convert.ToBase64String(new byte[16])));
    }
}
