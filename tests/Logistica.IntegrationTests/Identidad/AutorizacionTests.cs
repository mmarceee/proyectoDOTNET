using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests.Identidad;

// Autorización por perfil (letra, sección 6.3) con sesiones reales y sin el respaldo provisorio: cada operación la hace
// sólo el perfil que indica su caso de uso. El aislamiento entre operadores lo cubre AislamientoTests; acá se prueba el
// control dentro de un mismo operador.
public class AutorizacionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    private static CrearEnvioRequest UnEnvio() => new(
        new DestinatarioRequest("Ana Pérez", "099123456", null, null),
        new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
        [new BultoRequest(2, 30, 20, 10)]);

    [Fact]
    public async Task Sin_sesion_no_se_puede_crear_un_envio()
    {
        using var client = Cliente();

        var respuesta = await client.PostAsJsonAsync("/api/envios", UnEnvio());

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // CU-03: hasta elegir operador, el comercio no tiene inquilino y no puede operar.
    [Fact]
    public async Task El_comercio_sin_operador_elegido_no_puede_crear_un_envio()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");

        var respuesta = await client.PostAsJsonAsync("/api/envios", UnEnvio());

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    // CU-16: cancelar es del comercio (T17); el personal del operador no puede.
    [Fact]
    public async Task El_despachador_no_puede_cancelar_un_envio()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");

        var respuesta = await client.PostAsJsonAsync($"/api/envios/{Guid.NewGuid()}/cancelacion", new CancelarEnvioRequest(null));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    // CU-30: recibir bultos es del operario de depósito.
    [Fact]
    public async Task El_despachador_no_puede_recibir_bultos()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");

        var respuesta = await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest("ENV-NO-EXISTE-1", null));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_operario_de_deposito_si_puede_intentar_recibir_bultos()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "deposito@operador-demo.uy");

        var respuesta = await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest("ENV-NO-EXISTE-1", null));

        // Pasa la autorización; el código no existe, así que lo rechaza el caso de uso, no la política.
        Assert.NotEqual(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_comercio_con_operador_elegido_puede_crear_y_cancelar_su_envio()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(client, Modules.Administracion.Infrastructure.Persistence.DatosIniciales.OperadorDemoId);

        var creado = await client.PostAsJsonAsync("/api/envios", UnEnvio());
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var envio = await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>();

        var cancelado = await client.PostAsJsonAsync($"/api/envios/{envio!.Id}/cancelacion", new CancelarEnvioRequest(null));
        Assert.Equal(HttpStatusCode.NoContent, cancelado.StatusCode);
    }
}
