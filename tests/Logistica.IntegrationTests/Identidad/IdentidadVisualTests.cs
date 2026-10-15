using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests.Identidad;

// CU-07 de punta a punta: el administrador cambia la marca desde la página, con el logo. Cambia la identidad del
// operador Este, así que tiene su propia base (no comparte clase con MarcaDelOperadorTests).
public class IdentidadVisualTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Pagina = "/backoffice/identidad-visual";

    private static readonly byte[] UnPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    [Fact]
    public async Task El_administrador_guarda_colores_y_logo_y_se_ven_en_la_sesion()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "admin@envios-del-este.uy");

        var respuesta = await GuardarAsync(client, "#123456", ("image/png", UnPng));

        Assert.Contains("Se guardó la identidad visual.", await TextoAsync(respuesta));
        Assert.Contains("--operador-primario: #123456", await client.GetStringAsync("/cuenta/estilo.css"));

        var logo = await client.GetAsync("/cuenta/logo");
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(UnPng, await logo.Content.ReadAsByteArrayAsync());
    }

    // A2: un color que no es hexadecimal no se guarda.
    [Fact]
    public async Task Un_color_invalido_se_indica_y_no_se_guarda()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "admin@operador-demo.uy");

        var respuesta = await GuardarAsync(client, "rojo");

        Assert.Contains("tiene que ser hexadecimal", await TextoAsync(respuesta));
        Assert.Contains("--operador-primario: #1A56DB", await client.GetStringAsync("/cuenta/estilo.css"));
    }

    // A1: un logo de otro tipo no se guarda.
    [Fact]
    public async Task Un_logo_que_no_es_imagen_se_rechaza()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "admin@operador-demo.uy");

        var respuesta = await GuardarAsync(client, "#1A56DB", ("application/pdf", [1, 2, 3]));

        Assert.Contains("PNG, JPG o SVG", await TextoAsync(respuesta));
    }

    // A3: con poco contraste se guarda igual, con el aviso.
    [Fact]
    public async Task Con_poco_contraste_se_guarda_y_se_avisa()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "admin@envios-del-este.uy");

        var html = await TextoAsync(await GuardarAsync(client, "#FFEB3B"));

        Assert.Contains("Se guardó la identidad visual.", html);
        Assert.Contains("puede no leerse", html);
    }

    // Razor codifica las tildes (guardó → guard&#xF3;): se decodifica para buscar el texto que ve el usuario.
    private static async Task<string> TextoAsync(HttpResponseMessage respuesta)
    {
        return WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
    }

    // Manda el formulario como el navegador: multipart (por el archivo) con el token antifalsificación.
    private static async Task<HttpResponseMessage> GuardarAsync(
        HttpClient client, string colorPrimario, (string Tipo, byte[] Contenido)? logo = null)
    {
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(colorPrimario), "ColorPrimario" },
            { new StringContent("#ABCDEF"), "ColorSecundario" },
            { new StringContent("contacto@prueba.uy"), "EmailContacto" },
            { new StringContent(await Sesion.TokenAsync(client, Pagina)), "__RequestVerificationToken" },
        };

        if (logo is var (tipo, contenido))
        {
            var archivo = new ByteArrayContent(contenido);
            archivo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
            formulario.Add(archivo, "Logo", "logo");
        }

        var respuesta = await client.PostAsync(Pagina, formulario);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return respuesta;
    }
}
