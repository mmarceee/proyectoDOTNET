using System.Net;
using System.Text.RegularExpressions;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logistica.IntegrationTests.Identidad;

// Inicia sesión de verdad en las pruebas, como un navegador: pide la página para obtener el token antifalsificación,
// manda el formulario y el cliente guarda la cookie. Para las pruebas que no deben depender del respaldo provisorio.
internal static class Sesion
{
    // HTTPS porque la cookie es Secure; sin redirecciones automáticas, para poder ver a dónde manda cada respuesta.
    public static WebApplicationFactoryClientOptions Opciones =>
        new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false };

    // Cada usuario de demostración entra por la página de su aplicación. Sin contraseña, la de demostración.
    public static async Task<HttpResponseMessage> IngresarAsync(HttpClient client, string email, string? contrasena = null)
    {
        var pagina = email == "comercio@tiendademo.uy" ? "/portal/ingresar"
            : email.StartsWith("repartidor@") ? "/repartidor/ingresar"
            : "/cuenta/ingresar";

        var respuesta = await EnviarFormularioAsync(client, pagina,
            new() { ["Email"] = email, ["Contrasena"] = contrasena ?? UsuariosIniciales.ContrasenaDemo });

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return respuesta;
    }

    // CU-03: el usuario de comercio elige el operador de la sesión.
    public static Task<HttpResponseMessage> ElegirOperadorAsync(HttpClient client, Guid operadorId)
    {
        return EnviarFormularioAsync(client, "/cuenta/operador", new() { ["operadorId"] = operadorId.ToString() });
    }

    // Un formulario de una página, con su token antifalsificación (que se pide a la misma página).
    public static async Task<HttpResponseMessage> EnviarFormularioAsync(
        HttpClient client, string pagina, Dictionary<string, string> campos)
    {
        campos["__RequestVerificationToken"] = await TokenAsync(client, pagina);
        return await client.PostAsync(pagina, new FormUrlEncodedContent(campos));
    }

    // El token antifalsificación de un formulario: se pide la página y se lee del HTML.
    public static async Task<string> TokenAsync(HttpClient client, string pagina)
    {
        var html = await client.GetStringAsync(pagina);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, $"La página {pagina} no tiene el token antifalsificación.");
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }
}
