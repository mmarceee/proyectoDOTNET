using System.Net;
using Logistica.IntegrationTests.Identidad;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests.Deposito;

// CU-30 desde el Backoffice, con sesiones reales y sin el respaldo provisorio: la pantalla de recepción exige la misma
// política que la API (OperarioDeposito), aunque el middleware deje entrar a todo el personal al Backoffice.
public sealed class RecepcionPaginaAutorizacionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Pagina = "/backoffice/deposito/recepcion";

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    [Fact]
    public async Task El_operario_de_deposito_abre_la_pantalla_de_recepcion()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "deposito@operador-demo.uy");

        var respuesta = await client.GetAsync(Pagina);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("despacho@operador-demo.uy")]
    [InlineData("admin@operador-demo.uy")]
    public async Task El_resto_del_personal_no_puede_recibir_bultos_desde_el_Backoffice(string email)
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, email);

        var respuesta = await client.GetAsync(Pagina);

        // Con sesión pero sin el perfil: 403, sin mostrar la pantalla (no hay página de acceso denegado).
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
