using Microsoft.Playwright;

namespace Logistica.E2ETests;

// Iniciar sesión como lo hace una persona: completar el correo y la contraseña y tocar "Ingresar".
internal static class Ingreso
{
    // Backoffice (personal del operador): /cuenta/ingresar. Portal del comercio: /portal/ingresar.
    public static async Task ComoAsync(IPage pagina, string paginaDeIngreso, string correo)
    {
        await pagina.GotoAsync(paginaDeIngreso);
        await pagina.GetByLabel("Correo").FillAsync(correo);
        await pagina.GetByLabel("Contraseña").FillAsync(AplicacionE2E.Contrasena);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await pagina.WaitForURLAsync(url => !url.Contains("/ingresar", StringComparison.OrdinalIgnoreCase));
    }
}
