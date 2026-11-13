using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests.Identidad;

// El Backoffice muestra el perfil de la sesión con su distintivo (clase de color e ícono SVG) en la barra de todas las
// páginas, lo saluda en el Inicio ("Bienvenido a Despacho") y usa su ícono en la pestaña del navegador, para distinguir
// administración, despacho y depósito.
public class PerfilEnBackofficeTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Theory]
    [InlineData("admin@operador-demo.uy", "Administración", "perfil-administrador", "#5b21b6")]
    [InlineData("despacho@operador-demo.uy", "Despacho", "perfil-despacho", "#0f766e")]
    [InlineData("deposito@operador-demo.uy", "Depósito", "perfil-deposito", "#b45309")]
    [InlineData("admin@plataforma.uy", "Administración general", "perfil-general", "#1e3a8a")]
    public async Task Cada_perfil_se_muestra_con_su_nombre_su_distintivo_y_su_icono_de_pestana(
        string email, string area, string clase, string color)
    {
        using var client = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
            .CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, email);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/backoffice"));

        Assert.Contains($"perfil-usuario {clase}", html);
        Assert.Matches($@"Bienvenido a\s*<span class=""perfil-usuario perfil-usuario-grande {clase}"">\s*<svg[\s\S]*?</svg>\s*{area}\s*</span>", html);
        // El ícono de la pestaña es el SVG del perfil, con su color, como data URI.
        Assert.Contains("rel=\"icon\" type=\"image/svg+xml\" href=\"data:image/svg+xml,", html);
        Assert.Contains(Uri.EscapeDataString($"fill=\"{color}\""), html);
        // Ningún otro perfil aparece en la sesión.
        Assert.Single(new[] { "perfil-administrador", "perfil-despacho", "perfil-deposito", "perfil-general" },
            c => html.Contains($"perfil-usuario {c}"));
    }
}
