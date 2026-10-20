using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests.Identidad;

public sealed class IngresoInvalidoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory.WithWebHostBuilder(b =>
        b.UseSetting("Identity:PermitirTenantProvisorio", "false")).CreateClient(Sesion.Opciones);

    [Theory]
    [InlineData("/repartidor/ingresar", "repartidor@operador-demo.uy")]
    [InlineData("/portal/ingresar", "comercio@tiendademo.uy")]
    [InlineData("/cuenta/ingresar", "despacho@operador-demo.uy")]
    public async Task Una_contrasena_incorrecta_muestra_el_error_en_el_formulario(string pagina, string correo)
    {
        using var client = Cliente();
        var respuesta = await Sesion.EnviarFormularioAsync(client, pagina,
            new() { ["Email"] = correo, ["Contrasena"] = "Clave-Incorrecta-Prueba" });
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains("El correo o la contraseña no son correctos.", html);
        Assert.Contains("<form", html);
        Assert.DoesNotContain("value=\"Clave-Incorrecta-Prueba\"", html);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/repartidor/sesion")).StatusCode);
    }

    [Theory]
    [InlineData("/repartidor/ingresar")]
    [InlineData("/portal/ingresar")]
    [InlineData("/cuenta/ingresar")]
    public async Task Un_formulario_sin_token_se_renueva_sin_aceptar_las_credenciales(string pagina)
    {
        using var client = Cliente();
        var respuesta = await client.PostAsync(pagina, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "repartidor@operador-demo.uy", ["Contrasena"] = "Demo-2026!"
        }));
        await ExigirFormularioRenovadoAsync(client, pagina, respuesta);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/repartidor/sesion")).StatusCode);
    }

    [Theory]
    [InlineData("/repartidor/ingresar")]
    [InlineData("/portal/ingresar")]
    [InlineData("/cuenta/ingresar")]
    public async Task Cambiar_la_sesion_en_otra_pestana_renueva_el_formulario_anterior(string pagina)
    {
        using var client = Cliente();
        var tokenAnterior = await Sesion.TokenAsync(client, pagina);
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");
        var respuesta = await client.PostAsync(pagina, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenAnterior,
            ["Email"] = "repartidor@operador-demo.uy", ["Contrasena"] = "Demo-2026!"
        }));
        await ExigirFormularioRenovadoAsync(client, pagina, respuesta);
        // El formulario rechazado no cambió el usuario ni abrió la sesión del repartidor.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/repartidor/sesion")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/planificacion/rutas")).StatusCode);
    }

    private static async Task ExigirFormularioRenovadoAsync(HttpClient client, string pagina, HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(pagina + "?formularioVencido=true", respuesta.Headers.Location!.ToString());
        var paginaRenovada = await client.GetAsync(respuesta.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, paginaRenovada.StatusCode);
        var html = WebUtility.HtmlDecode(await paginaRenovada.Content.ReadAsStringAsync());
        Assert.Contains("El formulario de ingreso venció o la sesión cambió en otra pestaña.", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
    }
}
