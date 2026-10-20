using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Logistica.Http.Contracts.Avisos;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Infrastructure.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

public sealed class EnviarPruebaAvisoApiTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Ruta = "/api/avisos/suscripciones";
    private static readonly string Clave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData(202, true)]
    [InlineData(204, true)]
    [InlineData(302, false)]
    [InlineData(500, false)]
    public async Task Prueba_real_de_api_llega_firmada_al_receptor_y_muestra_su_codigo(int codigo, bool exitosa)
    {
        var receptor = new Receptor(codigo);
        using var api = CrearApi(receptor);
        using var cliente = await ClienteAsync(api);
        var suscripcion = await CrearAsync(cliente);
        receptor.Secreto = suscripcion.SecretoFirma;

        using var respuesta = await cliente.PostAsync($"{Ruta}/{suscripcion.Id}/prueba", null);
        respuesta.EnsureSuccessStatusCode();
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
        var resultado = (await respuesta.Content.ReadFromJsonAsync<ResultadoPruebaAvisoResponse>())!;
        Assert.Equal(exitosa, resultado.Exitosa);
        Assert.Equal(codigo, resultado.CodigoRespuesta);
        Assert.Equal(receptor.MensajeId, resultado.MensajeId);
        Assert.Equal(1, receptor.Intentos);
        Assert.Equal(suscripcion.SecretoFirma, receptor.Secreto);
        Assert.DoesNotContain(suscripcion.SecretoFirma, await respuesta.Content.ReadAsStringAsync());
        var detalle = (await cliente.GetFromJsonAsync<SuscripcionAvisoResponse>($"{Ruta}/{suscripcion.Id}"))!;
        Assert.True(detalle.Activa);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fallo_de_red_o_timeout_se_muestra_como_resultado_sin_reintentar(bool timeout)
    {
        var receptor = new Receptor(200) { Timeout = timeout, FalloConexion = !timeout };
        using var api = CrearApi(receptor, limite: timeout ? TimeSpan.FromMilliseconds(50) : null);
        using var cliente = await ClienteAsync(api);
        var suscripcion = await CrearAsync(cliente);
        receptor.Secreto = suscripcion.SecretoFirma;
        using var respuesta = await cliente.PostAsync($"{Ruta}/{suscripcion.Id}/prueba", null);
        respuesta.EnsureSuccessStatusCode();
        var resultado = (await respuesta.Content.ReadFromJsonAsync<ResultadoPruebaAvisoResponse>())!;
        Assert.False(resultado.Exitosa);
        Assert.Null(resultado.CodigoRespuesta);
        Assert.NotEmpty(resultado.Error!);
        Assert.Equal(1, receptor.Intentos);
    }

    [Fact]
    public async Task Inactiva_y_ajena_no_envian_y_la_regeneracion_se_usa_en_la_siguiente_prueba()
    {
        var receptor = new Receptor(200);
        using var api = CrearApi(receptor);
        using var cliente = await ClienteAsync(api);
        var suscripcion = await CrearAsync(cliente);
        using var regeneracion = await cliente.PostAsync($"{Ruta}/{suscripcion.Id}/secreto", null);
        regeneracion.EnsureSuccessStatusCode();
        var nueva = (await regeneracion.Content.ReadFromJsonAsync<SecretoSuscripcionAvisoResponse>())!;
        Assert.NotEqual(suscripcion.SecretoFirma, nueva.SecretoFirma);
        receptor.Secreto = nueva.SecretoFirma;
        using (var prueba = await cliente.PostAsync($"{Ruta}/{suscripcion.Id}/prueba", null)) prueba.EnsureSuccessStatusCode();
        Assert.Equal(1, receptor.Intentos);

        await Sesion.ElegirOperadorAsync(cliente, DatosIniciales.OperadorEsteId);
        await TokenAsync(cliente);
        using (var ajena = await cliente.PostAsync($"{Ruta}/{suscripcion.Id}/prueba", null))
            Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        await Sesion.ElegirOperadorAsync(cliente, DatosIniciales.OperadorDemoId);
        await TokenAsync(cliente);
        using (var baja = await cliente.PostAsync($"{Ruta}/{suscripcion.Id}/desactivacion", null)) baja.EnsureSuccessStatusCode();
        using (var inactiva = await cliente.PostAsync($"{Ruta}/{suscripcion.Id}/prueba", null))
            Assert.Equal(HttpStatusCode.BadRequest, inactiva.StatusCode);
        Assert.Equal(1, receptor.Intentos);
    }

    private WebApplicationFactory<Program> CrearApi(Receptor receptor, TimeSpan? limite = null) =>
        factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("Seguimiento:Avisos:ClaveCifrado", Clave)
            .ConfigureServices(s => s.AddHttpClient<IEmisorAvisos, EmisorAvisosHttp>(c => c.Timeout = limite ?? TimeSpan.FromSeconds(10))
                .ConfigurePrimaryHttpMessageHandler(() => receptor)));

    private static async Task<HttpClient> ClienteAsync(WebApplicationFactory<Program> api)
    {
        var cliente = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(cliente, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(cliente, DatosIniciales.OperadorDemoId);
        await TokenAsync(cliente);
        return cliente;
    }

    private static async Task TokenAsync(HttpClient cliente)
    {
        var token = (await cliente.GetFromJsonAsync<TokenAntifalsificacionResponse>($"{Ruta}/antifalsificacion"))!.Token;
        cliente.DefaultRequestHeaders.Remove("RequestVerificationToken");
        cliente.DefaultRequestHeaders.Add("RequestVerificationToken", token);
    }

    private static async Task<SecretoSuscripcionAvisoResponse> CrearAsync(HttpClient cliente)
    {
        using var respuesta = await cliente.PostAsJsonAsync(Ruta, new CrearSuscripcionAvisoRequest(
            "https://comercio.example/avisos", ["CambioDeEstado"]));
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<SecretoSuscripcionAvisoResponse>())!;
    }

    // Receptor de prueba en memoria: verifica la firma de forma independiente del firmador del emisor.
    private sealed class Receptor(int codigo) : HttpMessageHandler
    {
        public string Secreto { get; set; } = "";
        public int Intentos { get; private set; }
        public Guid MensajeId { get; private set; }
        public bool Timeout { get; init; }
        public bool FalloConexion { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Intentos++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://comercio.example/avisos", request.RequestUri!.AbsoluteUri);
            var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
            var fecha = request.Headers.GetValues("X-Aviso-Fecha").Single();
            var firma = request.Headers.GetValues("X-Aviso-Firma").Single();
            var entrada = Encoding.UTF8.GetBytes(fecha + ".").Concat(bytes).ToArray();
            var esperada = HMACSHA256.HashData(Convert.FromBase64String(Secreto), entrada);
            Assert.StartsWith("v1=", firma);
            Assert.True(CryptographicOperations.FixedTimeEquals(esperada, Convert.FromHexString(firma[3..])));
            Assert.InRange(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - long.Parse(fecha)), 0, 300);
            using var json = JsonDocument.Parse(bytes);
            MensajeId = json.RootElement.GetProperty("id").GetGuid();
            Assert.True(json.RootElement.GetProperty("prueba").GetBoolean());
            Assert.Equal("PRUEBA", json.RootElement.GetProperty("numeroEnvio").GetString());
            Assert.Equal("Admitido", json.RootElement.GetProperty("estado").GetString());
            Assert.Equal("CambioDeEstado", json.RootElement.GetProperty("tipo").GetString());
            Assert.DoesNotContain(Secreto, Encoding.UTF8.GetString(bytes));
            if (Timeout) await Task.Delay(System.Threading.Timeout.Infinite, ct);
            if (FalloConexion) throw new HttpRequestException("Error de conexión simulado.");
            var respuesta = new HttpResponseMessage((HttpStatusCode)codigo);
            if (codigo == 302) respuesta.Headers.Location = new Uri("https://127.0.0.1/privado");
            return respuesta;
        }
    }
}
