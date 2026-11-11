using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Logistica.E2ETests;

// Flujo crítico 2: el comercio crea un envío en el Portal y el operador lo ve en el Backoffice, pero otro operador no
// (CU-10, CU-13 y el aislamiento entre inquilinos de la sección 6.5).
// Flujo crítico 3: el destinatario abre el enlace de seguimiento, sin usuario, y ve su envío (CU-60, RF 25).
[Collection(ColeccionE2E.Nombre)]
public class EnvioE2ETests(AplicacionE2E app)
{
    [Fact]
    public async Task El_envio_que_crea_el_comercio_lo_ve_su_operador_y_no_otro()
    {
        var numero = await Pasos.CrearEnvioEnPortalAsync(app, "Ana Portal");

        var operador = await app.NuevaPaginaAsync();
        await Ingreso.ComoAsync(operador, "/cuenta/ingresar", Pasos.Despachador);
        await operador.GotoAsync($"/backoffice/envios?Texto={numero}");
        var fila = operador.GetByRole(AriaRole.Row).Filter(new() { HasText = numero });
        await Expect(fila).ToContainTextAsync("Ana Portal");
        await Expect(fila).ToContainTextAsync("Admitido");

        var otro = await app.NuevaPaginaAsync();
        await Ingreso.ComoAsync(otro, "/cuenta/ingresar", Pasos.DespachadorDeOtroOperador);
        await otro.GotoAsync($"/backoffice/envios?Texto={numero}");
        await Expect(otro.GetByText("No hay envíos para mostrar.")).ToBeVisibleAsync();
        await Expect(otro.GetByText(numero)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task El_destinatario_abre_el_enlace_de_seguimiento_y_ve_su_envio()
    {
        var numero = await Pasos.CrearEnvioEnPortalAsync(app, "Beatriz Seguimiento");

        // El despachador obtiene el enlace firmado desde el Backoffice, como para mandárselo al destinatario.
        var operador = await app.NuevaPaginaAsync();
        await Ingreso.ComoAsync(operador, "/cuenta/ingresar", Pasos.Despachador);
        await operador.GotoAsync($"/backoffice/seguimiento/{numero}");
        var enlace = await operador.Locator("#enlace").InputValueAsync();
        Assert.Matches(new Regex("/seguimiento/[^/]+$"), enlace);

        // El destinatario no tiene sesión: una ventana nueva, sin cookies.
        var destinatario = await app.NuevaPaginaAsync();
        await destinatario.GotoAsync(enlace);
        await Expect(destinatario.GetByText($"Hola, Beatriz")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(destinatario.GetByText(numero).First).ToBeVisibleAsync();
        await Expect(destinatario.GetByRole(AriaRole.Heading, new() { Name = "Historial" })).ToBeVisibleAsync();

        // Un enlace alterado no muestra nada del envío (CU-60, A1).
        await destinatario.GotoAsync(enlace + "x");
        await Expect(destinatario.GetByRole(AriaRole.Heading, new() { Name = "Enlace no válido" })).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
}
