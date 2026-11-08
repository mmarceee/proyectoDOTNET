using System.Net;
using Logistica.IntegrationTests.BuildingBlocks;
using Logistica.IntegrationTests.Identidad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static Logistica.IntegrationTests.Administracion.PaginaTarifario;

namespace Logistica.IntegrationTests.Administracion;

// Aclaración de caché (revisión del cambio de requerimientos, sección 9; ADR-0006): dos APIs completas con el mismo
// PostgreSQL y el mismo Valkey. Un precio publicado desde una rige enseguida en la otra, aunque las dos tuvieran el
// tarifario anterior en la caché. Si cada API tuviera su propia caché (la memoria del proceso), la segunda seguiría
// cobrando el precio viejo hasta que venciera su puntero. Tiene su propia base, porque publica una versión nueva.
public sealed class TarifarioEntreDosApisTests(PostgresApiFactory factory, ValkeyFixture valkey)
    : IClassFixture<PostgresApiFactory>, IClassFixture<ValkeyFixture>
{
    [Fact]
    public async Task Un_precio_publicado_en_una_API_rige_enseguida_en_la_otra()
    {
        // Cada WithWebHostBuilder es un host propio, con su contenedor de servicios y su conexión a Valkey.
        await using var apiA = Api();
        await using var apiB = Api();
        using var adminA = await AdminAsync(apiA);
        using var adminB = await AdminAsync(apiB);

        // Antes: las dos cobran $150 con la versión 1, y la vigente queda en la caché.
        Assert.Contains("$ 150 con la versión 1", await CalcularAsync(adminA, ZonaDemo, "Estandar"));
        Assert.Contains("$ 150 con la versión 1", await CalcularAsync(adminB, ZonaDemo, "Estandar"));

        // Se publica desde la API A: Estándar de 0 a 5 kg pasa de $150 a $175.
        await AccionAsync(adminA, Pagina, "Nueva", []);
        await QuitarReglaAsync(adminA, "$ 150");
        await AccionAsync(adminA, Borrador, "Regla", Regla(ZonaDemo, "Estandar", "0", "5", "0", "0.1", "175"));
        var publicada = await AccionAsync(adminA, Borrador, "Publicar", [new("ConfirmaSinTarifa", "true")]);
        Assert.Equal(HttpStatusCode.Redirect, publicada.StatusCode);

        // La API B, sin esperas, ya cobra el precio nuevo: la publicación borró el puntero en el Valkey compartido.
        Assert.Contains("$ 175 con la versión 2", await CalcularAsync(adminB, ZonaDemo, "Estandar"));
    }

    private WebApplicationFactory<Program> Api() => factory.WithWebHostBuilder(b => b
        .UseSetting("ConnectionStrings:Valkey", valkey.ConnectionString)
        .UseSetting("Identity:PermitirTenantProvisorio", "false"));

    // Cada API con su propia sesión del administrador de Demo, como dos navegadores.
    private static async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> api)
    {
        var client = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, "admin@operador-demo.uy");
        return client;
    }
}
