using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Logistica.E2ETests;

// Pasos que comparten varios flujos, hechos como los haría una persona desde la pantalla.
internal static class Pasos
{
    public const string Despachador = "despacho@operador-demo.uy";
    public const string DespachadorDeOtroOperador = "despacho@envios-del-este.uy";
    public const string Comercio = "comercio@tiendademo.uy";

    // CU-10 desde el Portal del comercio: entra, elige el operador de la sesión (CU-03), completa el formulario y
    // confirma. Devuelve el número del envío que muestra la pantalla (por ejemplo ENV-000001).
    public static async Task<string> CrearEnvioEnPortalAsync(AplicacionE2E app, string destinatario)
    {
        var pagina = await app.NuevaPaginaAsync();
        await Ingreso.ComoAsync(pagina, "/portal/ingresar", Comercio);

        // El comercio trabaja con más de un operador: elige con cuál en esta sesión (CU-03).
        if (pagina.Url.Contains("/cuenta/operador", StringComparison.OrdinalIgnoreCase))
        {
            await pagina.GetByRole(AriaRole.Button, new() { Name = "Operador Demo" }).ClickAsync();
            await pagina.WaitForURLAsync(url => !url.Contains("/cuenta/operador", StringComparison.OrdinalIgnoreCase));
        }

        // El Portal es una aplicación Blazor WebAssembly: la primera vez descarga el runtime, por eso la espera es mayor.
        await pagina.GotoAsync("/portal/envios/nuevo");
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "Nuevo envío" })).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await pagina.GetByLabel("Nombre", new() { Exact = true }).FillAsync(destinatario);
        await pagina.GetByLabel("Teléfono", new() { Exact = true }).FillAsync("099123456");
        await pagina.GetByLabel("Calle", new() { Exact = true }).FillAsync("Av. Italia");
        await pagina.GetByLabel("Número", new() { Exact = true }).FillAsync("1234");
        await pagina.GetByLabel("Localidad", new() { Exact = true }).FillAsync("Montevideo");
        // 11300 es de la zona de Operador Demo (datos iniciales): sin zona el envío no se admite (CU-10, A2).
        await pagina.GetByLabel("Código postal", new() { Exact = true }).FillAsync("11300");
        await pagina.GetByLabel("Peso del bulto 1").FillAsync("2");
        await pagina.GetByLabel("Largo del bulto 1").FillAsync("30");
        await pagina.GetByLabel("Ancho del bulto 1").FillAsync("20");
        await pagina.GetByLabel("Alto del bulto 1").FillAsync("10");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Confirmar" }).ClickAsync();

        var aviso = pagina.GetByRole(AriaRole.Status);
        await Expect(aviso).ToContainTextAsync("Se creó el envío");
        // La ventana del comercio se cierra al final de la corrida, así su traza queda grabada.
        return (await aviso.Locator("strong").InnerTextAsync()).Trim();
    }
}
