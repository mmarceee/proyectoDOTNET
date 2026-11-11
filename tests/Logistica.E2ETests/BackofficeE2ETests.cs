using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Logistica.E2ETests;

// Flujo crítico 1: el personal del operador entra al Backoffice y ve sólo lo de su operador (RF 1, ADR-0002).
[Collection(ColeccionE2E.Nombre)]
public class BackofficeE2ETests(AplicacionE2E app)
{
    [Fact]
    public async Task El_despachador_entra_y_ve_el_listado_de_envios()
    {
        var pagina = await app.NuevaPaginaAsync();

        await Ingreso.ComoAsync(pagina, "/cuenta/ingresar", "despacho@operador-demo.uy");
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Envíos", Exact = true }).First.ClickAsync();

        await Expect(pagina).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/backoffice/envios"));
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "Envíos", Exact = true })).ToBeVisibleAsync();
        await Expect(pagina.GetByText("despacho@operador-demo.uy")).ToBeVisibleAsync();
    }
}
