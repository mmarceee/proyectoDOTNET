using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests.Identidad;

public class GestionarComerciosTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory.WithWebHostBuilder(b =>
        b.UseSetting("Identity:PermitirTenantProvisorio", "false")).CreateClient(Sesion.Opciones);

    private async Task<HttpClient> AdminAsync(bool este = false)
    {
        var client = Cliente();
        await Sesion.IngresarAsync(client, este ? "admin@envios-del-este.uy" : "admin@operador-demo.uy");
        return client;
    }

    private static async Task<HttpResponseMessage> AltaAsync(HttpClient client, string documento, string nombre)
        => await Sesion.EnviarFormularioAsync(client, "/backoffice/comercios/nuevo",
            new() { ["DocumentoFiscal"] = documento, ["RazonSocial"] = nombre, ["EmailContacto"] = "contacto@tienda.uy" });

    private static async Task<Dictionary<string, string>> CamposAsync(HttpClient client, string url, string accion)
    {
        var html = await client.GetStringAsync(url);
        var version = Regex.Match(html, "name=\"Version\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(version);
        return new() { ["Version"] = version, ["accion"] = accion, ["EmailContacto"] = "nuevo@tienda.uy" };
    }

    [Fact]
    public async Task Alta_vinculacion_compartida_y_aislamiento()
    {
        using var demo = await AdminAsync();
        using var este = await AdminAsync(true);
        var alta = await AltaAsync(demo, "219876543210", "Comercio Compartido CU01");
        Assert.Equal(HttpStatusCode.Redirect, alta.StatusCode);
        var url = alta.Headers.Location!.ToString();
        Assert.Equal(HttpStatusCode.NotFound, (await este.GetAsync(url)).StatusCode);
        var ajeno = new Dictionary<string, string>
        {
            ["accion"] = "suspender", ["Version"] = "0",
            ["__RequestVerificationToken"] = await Sesion.TokenAsync(este, "/backoffice/comercios/nuevo")
        };
        Assert.Equal(HttpStatusCode.NotFound, (await este.PostAsync(url, new FormUrlEncodedContent(ajeno))).StatusCode);
        Assert.DoesNotContain("Comercio Compartido CU01", await este.GetStringAsync("/backoffice/comercios"));

        var busqueda = await Sesion.EnviarFormularioAsync(este, "/backoffice/comercios/nuevo?handler=Buscar",
            new() { ["DocumentoFiscal"] = "219876543210" });
        var texto = WebUtility.HtmlDecode(await busqueda.Content.ReadAsStringAsync());
        Assert.Contains("Comercio Compartido CU01", texto);
        Assert.DoesNotContain("field is required", texto);
        Assert.DoesNotContain("Operador Demo", texto);
        var vinculo = await AltaAsync(este, "219876543210", "No debe cambiar la razón social");
        Assert.Equal(url, vinculo.Headers.Location!.ToString());
        Assert.Contains("Comercio Compartido CU01", await este.GetStringAsync(url));

        var modificar = await Sesion.EnviarFormularioAsync(demo, url, await CamposAsync(demo, url, "contacto"));
        Assert.Equal(HttpStatusCode.Redirect, modificar.StatusCode);
        Assert.Contains("nuevo@tienda.uy", await demo.GetStringAsync(url));
        Assert.Contains("contacto@tienda.uy", await este.GetStringAsync(url));
        Assert.Contains("Comercio Compartido CU01", await demo.GetStringAsync("/backoffice/comercios?Buscar=219876543210"));
        Assert.DoesNotContain("Comercio Compartido CU01", await demo.GetStringAsync("/backoffice/comercios?Estado=Suspendida"));
    }

    [Fact]
    public async Task Estados_y_version_obsoleta_no_pisan_cambios()
    {
        using var admin = await AdminAsync();
        var alta = await AltaAsync(admin, "219876543211", "Estados CU01");
        var url = alta.Headers.Location!.ToString();
        var obsoleto = await CamposAsync(admin, url, "contacto");
        Assert.Equal(HttpStatusCode.Redirect, (await Sesion.EnviarFormularioAsync(admin, url,
            await CamposAsync(admin, url, "suspender"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Sesion.EnviarFormularioAsync(admin, url, obsoleto)).StatusCode);
        Assert.Contains("Suspendida", await admin.GetStringAsync(url));
        Assert.Equal(HttpStatusCode.Redirect, (await Sesion.EnviarFormularioAsync(admin, url,
            await CamposAsync(admin, url, "reactivar"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Sesion.EnviarFormularioAsync(admin, url,
            await CamposAsync(admin, url, "baja"))).StatusCode);
        Assert.Contains("Baja", await admin.GetStringAsync(url));
        Assert.DoesNotContain("Estados CU01", await admin.GetStringAsync("/backoffice/comercios?Estado=Activa"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Sesion.EnviarFormularioAsync(admin, url,
            new() { ["Version"] = "0", ["accion"] = "reactivar" })).StatusCode);
    }

    [Fact]
    public async Task Suspender_bloquea_altas_con_cookie_abierta_y_baja_cuenta_envios_pendientes()
    {
        using var admin = await AdminAsync();
        using var comercio = Cliente();
        await Sesion.IngresarAsync(comercio, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(comercio, DatosIniciales.OperadorDemoId);
        var pedido = new CrearEnvioRequest(new DestinatarioRequest("Ana", "099123456", null, null),
            new DireccionRequest("Calle", "10", "Montevideo", "Montevideo", "11000", null),
            [new BultoRequest(1, 10, 10, 10)]);
        Assert.Equal(HttpStatusCode.Created, (await comercio.PostAsJsonAsync("/api/envios", pedido)).StatusCode);
        var url = $"/backoffice/comercios/{DatosIniciales.ComercioDemoId}";
        var baja = await Sesion.EnviarFormularioAsync(admin, url, await CamposAsync(admin, url, "baja"));
        Assert.Equal(HttpStatusCode.BadRequest, baja.StatusCode);
        Assert.Contains("sin terminar", await baja.Content.ReadAsStringAsync());

        await Sesion.EnviarFormularioAsync(admin, url, await CamposAsync(admin, url, "suspender"));
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await comercio.PostAsJsonAsync("/api/envios", pedido)).StatusCode);
            using var este = Cliente();
            await Sesion.IngresarAsync(este, "comercio@tiendademo.uy");
            // Al quedar una sola relación activa, CU-03 la elige automáticamente al abrir la página.
            Assert.Equal(HttpStatusCode.Redirect, (await este.GetAsync("/cuenta/operador")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await este.PostAsJsonAsync("/api/envios", pedido)).StatusCode);
        }
        finally { await Sesion.EnviarFormularioAsync(admin, url, await CamposAsync(admin, url, "reactivar")); }
    }

    [Theory]
    [InlineData("despacho@operador-demo.uy")]
    [InlineData("deposito@operador-demo.uy")]
    [InlineData("repartidor@operador-demo.uy")]
    public async Task Solo_administradores(string email)
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/backoffice/comercios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/backoffice/comercios/nuevo")).StatusCode);
    }

    [Fact]
    public async Task Validacion_y_altas_simultaneas_no_duplican()
    {
        using var admin = await AdminAsync();
        var invalido = await AltaAsync(admin, "abc", "");
        Assert.NotEqual(HttpStatusCode.Redirect, invalido.StatusCode);
        Assert.Contains("12 d", WebUtility.HtmlDecode(await invalido.Content.ReadAsStringAsync()));
        var paso = await Sesion.EnviarFormularioAsync(admin, "/backoffice/comercios/nuevo?handler=Buscar",
            new() { ["DocumentoFiscal"] = "219876543213" });
        var formulario = await paso.Content.ReadAsStringAsync();
        Assert.Contains("action=\"/backoffice/comercios/nuevo\"", formulario);
        Assert.DoesNotContain("field is required", formulario);
        var sinContacto = await Sesion.EnviarFormularioAsync(admin, "/backoffice/comercios/nuevo",
            new() { ["DocumentoFiscal"] = "219876543213", ["RazonSocial"] = "Inválido" });
        Assert.Contains("correo de contacto", WebUtility.HtmlDecode(await sinContacto.Content.ReadAsStringAsync()));
        Assert.NotEqual(HttpStatusCode.Redirect, sinContacto.StatusCode);
        using var otro = await AdminAsync();
        var altas = await Task.WhenAll(AltaAsync(admin, "219876543212", "Concurrente CU01"),
            AltaAsync(otro, "219876543212", "Concurrente CU01"));
        Assert.Contains(altas, r => r.StatusCode == HttpStatusCode.Redirect);
        Assert.DoesNotContain(altas, r => (int)r.StatusCode >= 500);
        var lista = await admin.GetStringAsync("/backoffice/comercios?Buscar=219876543212");
        Assert.Single(Regex.Matches(lista, "<td>219876543212</td>"));
    }
}
