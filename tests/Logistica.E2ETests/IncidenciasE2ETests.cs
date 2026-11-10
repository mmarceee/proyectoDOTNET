using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Logistica.E2ETests;

// Flujo crítico 4: el despachador registra un problema de un envío y lo sigue hasta resolverlo (CU-22).
[Collection(ColeccionE2E.Nombre)]
public class IncidenciasE2ETests(AplicacionE2E app)
{
    [Fact]
    public async Task El_despachador_registra_atiende_y_resuelve_una_incidencia()
    {
        var numero = await Pasos.CrearEnvioEnPortalAsync(app, "Carla Incidencia");
        var pagina = await app.NuevaPaginaAsync();
        await Ingreso.ComoAsync(pagina, "/cuenta/ingresar", Pasos.Despachador);

        // Pasos 1 a 3: desde el detalle del envío.
        await pagina.GotoAsync($"/backoffice/envios/{numero}");
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Nueva incidencia" }).ClickAsync();
        await pagina.GetByLabel("Tipo").SelectOptionAsync(new SelectOptionValue { Label = "Daño en bulto" });
        await pagina.GetByLabel("Descripción").FillAsync($"Caja aplastada en {numero}");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Registrar incidencia" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = $"Envío {numero}" })).ToBeVisibleAsync();
        await Expect(pagina.GetByText($"Caja aplastada en {numero}")).ToBeVisibleAsync();

        // Paso 4, desde el listado del menú.
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Incidencias", Exact = true }).First.ClickAsync();
        var fila = pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = $"Caja aplastada en {numero}" });
        await Expect(fila).ToContainTextAsync("Abierta");
        await fila.GetByRole(AriaRole.Button, new() { Name = "Pasar a en proceso" }).ClickAsync();
        await Expect(fila).ToContainTextAsync("En proceso");

        // A2: sin resolución no se resuelve.
        await fila.GetByRole(AriaRole.Button, new() { Name = "Resolver" }).ClickAsync();
        await Expect(pagina.GetByText("Indicá cómo se resolvió la incidencia.")).ToBeVisibleAsync();
        await Expect(fila).ToContainTextAsync("En proceso");

        // Paso 5.
        await fila.GetByRole(AriaRole.Textbox).FillAsync("Se repuso la mercadería");
        await fila.GetByRole(AriaRole.Button, new() { Name = "Resolver" }).ClickAsync();
        await Expect(fila).ToContainTextAsync("Resuelta");
        await Expect(fila).ToContainTextAsync("Se repuso la mercadería");
    }
}
