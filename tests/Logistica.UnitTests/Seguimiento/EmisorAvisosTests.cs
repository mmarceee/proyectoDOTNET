using System.Net;
using System.Text;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Infrastructure.Http;
using Logistica.Modules.Seguimiento.Infrastructure.Security;

namespace Logistica.UnitTests.Seguimiento;

public class EmisorAvisosTests
{
    private const string Secreto = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private static readonly DateTimeOffset Fecha = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);
    private static MensajeAviso Mensaje() => new(Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "CambioDeEstado", Fecha, "PRUEBA", "Admitido", true);

    [Fact]
    public void Firma_coincide_con_vector_independiente_y_cambia_si_se_altera_cuerpo_o_fecha()
    {
        var cuerpo = FirmadorAvisos.Serializar(Mensaje());
        Assert.Equal("{\"id\":\"11111111-1111-1111-1111-111111111111\",\"tipo\":\"CambioDeEstado\",\"fechaEvento\":\"2026-10-20T15:00:00+00:00\",\"numeroEnvio\":\"PRUEBA\",\"estado\":\"Admitido\",\"prueba\":true}", Encoding.UTF8.GetString(cuerpo));
        Assert.Equal("1792508400", FirmadorAvisos.Fecha(Fecha));
        var firma = FirmadorAvisos.Firmar(cuerpo, "1792508400", Secreto);
        Assert.Equal("v1=19be04bc86322406b0a4a85cbc0d9f49e4b412704a1d8b6c59af55fdd31f441d", firma);
        Assert.NotEqual(firma, FirmadorAvisos.Firmar(cuerpo, "1792508401", Secreto));
        Assert.NotEqual(firma, FirmadorAvisos.Firmar([.. cuerpo, (byte)' '], "1792508400", Secreto));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(302, false)]
    [InlineData(401, false)]
    [InlineData(500, false)]
    public async Task Un_solo_post_firmado_devuelve_el_codigo_sin_leer_el_cuerpo_del_receptor(int codigo, bool exitosa)
    {
        var intentos = 0;
        using var transporte = new Receptor(async (request, ct) =>
        {
            intentos++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            var cuerpo = await request.Content.ReadAsByteArrayAsync(ct);
            var fecha = Assert.Single(request.Headers.GetValues("X-Aviso-Fecha"));
            Assert.Equal("1792508400", fecha);
            Assert.Equal(FirmadorAvisos.Firmar(cuerpo, fecha, Secreto), Assert.Single(request.Headers.GetValues("X-Aviso-Firma")));
            return new((HttpStatusCode)codigo) { Content = new CuerpoQueNoSeDebeLeer() };
        });
        using var http = new HttpClient(transporte);
        var resultado = await new EmisorAvisosHttp(http, new Reloj()).EnviarAsync("https://comercio.example/avisos", Secreto, Mensaje(), default);
        Assert.Equal(1, intentos);
        Assert.Equal(codigo, resultado.CodigoRespuesta);
        Assert.Equal(exitosa, resultado.Exitosa);
        Assert.Equal(exitosa, resultado.Error is null);
    }

    [Fact]
    public async Task Timeout_y_error_de_red_se_informan_sin_reintentar_y_cancelacion_del_cliente_se_propaga()
    {
        using var lento = new Receptor(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.OK);
        });
        using var http = new HttpClient(lento) { Timeout = TimeSpan.FromMilliseconds(50) };
        var resultado = await new EmisorAvisosHttp(http, new Reloj()).EnviarAsync("https://comercio.example/avisos", Secreto, Mensaje(), default);
        Assert.False(resultado.Exitosa);
        Assert.Null(resultado.CodigoRespuesta);
        Assert.Contains("10 segundos", resultado.Error);
        using var cancelada = new CancellationTokenSource();
        cancelada.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EmisorAvisosHttp(http, new Reloj())
            .EnviarAsync("https://comercio.example/avisos", Secreto, Mensaje(), cancelada.Token));

        using var desconectado = new Receptor((_, _) => throw new HttpRequestException("detalle interno"));
        using var sinRed = new HttpClient(desconectado);
        var fallo = await new EmisorAvisosHttp(sinRed, new Reloj()).EnviarAsync("https://comercio.example/avisos", Secreto, Mensaje(), default);
        Assert.False(fallo.Exitosa);
        Assert.Null(fallo.CodigoRespuesta);
        Assert.DoesNotContain("detalle interno", fallo.Error!);
    }

    [Theory]
    [InlineData("http://comercio.example/avisos")]
    [InlineData("https://usuario:clave@comercio.example/avisos")]
    [InlineData("https://comercio.example/avisos#fragmento")]
    public async Task Url_no_permitida_no_llega_al_transporte(string url)
    {
        using var transporte = new Receptor((_, _) => throw new InvalidOperationException("No debe enviar."));
        using var http = new HttpClient(transporte);
        Assert.False((await new EmisorAvisosHttp(http, new Reloj()).EnviarAsync(url, Secreto, Mensaje(), default)).Exitosa);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("64:ff9b::7f00:1")]
    public void Bloquea_destinos_no_publicos_e_incluso_resoluciones_dns_mixtas(string direccion)
    {
        var ip = IPAddress.Parse(direccion);
        Assert.False(ConectorAvisosHttp.EsPublica(ip));
        Assert.Throws<HttpRequestException>(() => ConectorAvisosHttp.ValidarDirecciones([IPAddress.Parse("8.8.8.8"), ip]));
    }

    [Fact]
    public async Task Transporte_real_bloquea_loopback_sin_aceptar_redirecciones_proxy_ni_cookies()
    {
        using var handler = ConectorAvisosHttp.CrearHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://127.0.0.1:1"));
        Assert.Throws<HttpRequestException>(() => ConectorAvisosHttp.ValidarDirecciones([]));
        ConectorAvisosHttp.ValidarDirecciones([IPAddress.Parse("8.8.8.8"), IPAddress.Parse("2606:4700:4700::1111")]);
    }

    private sealed class Reloj : TimeProvider { public override DateTimeOffset GetUtcNow() => Fecha; }
    private sealed class Receptor(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => responder(request, ct);
    }
    private sealed class CuerpoQueNoSeDebeLeer : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
