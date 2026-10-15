using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests.Identidad;

// CU-07: cada operador ve su propia marca, con sesiones reales y sin el respaldo provisorio. Sólo lee la identidad
// de los datos iniciales; las pruebas que la cambian están en IdentidadVisualTests, con su propia base.
public class MarcaDelOperadorTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    [Fact]
    public async Task Cada_operador_ve_su_marca_y_no_la_del_otro()
    {
        using var demo = Cliente();
        await Sesion.IngresarAsync(demo, "admin@operador-demo.uy");
        using var este = Cliente();
        await Sesion.IngresarAsync(este, "admin@envios-del-este.uy");

        var estiloDemo = await demo.GetStringAsync("/cuenta/estilo.css");
        var estiloEste = await este.GetStringAsync("/cuenta/estilo.css");

        Assert.Contains("--operador-primario: #1A56DB", estiloDemo);
        Assert.Contains("--operador-primario: #047857", estiloEste);
        Assert.DoesNotContain("#1A56DB", estiloEste);
    }

    [Fact]
    public async Task Sin_sesion_los_estilos_son_los_de_por_defecto_y_no_hay_logo()
    {
        using var client = Cliente();

        var estilo = await client.GetStringAsync("/cuenta/estilo.css");

        Assert.DoesNotContain("--operador-primario", estilo);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/cuenta/logo")).StatusCode);
    }

    [Fact]
    public async Task El_despachador_no_puede_abrir_la_identidad_visual()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");

        var respuesta = await client.GetAsync("/backoffice/identidad-visual");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
