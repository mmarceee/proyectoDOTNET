using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Administracion;

// RF 33 de punta a punta (decisiones-entrega-parcial.md, sección 5): el administrador prende la entrega parcial en una
// versión nueva de reglas (CU-06) y Envíos la recibe por el contrato. Clase propia, con su base: publica reglas de Demo.
public class EntregaParcialReglasTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Pagina = "/backoffice/reglas";
    private const string Borrador = "/backoffice/reglas/borrador";

    [Fact]
    public async Task Prender_la_entrega_parcial_rige_para_los_envios_nuevos_y_no_para_los_anteriores()
    {
        // Un envío admitido con la versión 1, que no admite la parcial (datos iniciales, como antes de RF 33).
        var anterior = await CrearEnvioAsync();
        using var demo = await AdminAsync("admin@operador-demo.uy");
        await AccionAsync(demo, Pagina, "Nueva", []);

        // A1: con la política de recargo, un recargo de cero no deja publicar.
        await AccionAsync(demo, Borrador, "Valores", Valores("NuevaVisitaConRecargo", "0"));
        var sinRecargo = await TextoAsync(await AccionAsync(demo, Borrador, "Publicar", []));
        Assert.Contains("El recargo de la nueva visita tiene que ser mayor que cero.", sinRecargo);

        // El recargo acepta coma decimal, como los montos del tarifario.
        await AccionAsync(demo, Borrador, "Valores", Valores("NuevaVisitaConRecargo", "150,50"));
        Assert.Equal(HttpStatusCode.Redirect, (await AccionAsync(demo, Borrador, "Publicar", [])).StatusCode);

        var detalle = await TextoAsync(await demo.GetAsync($"{Pagina}/2"));
        Assert.Contains("Entrega parcial</dt><dd class=\"col-sm-8\">Se admite<", detalle);
        Assert.Contains("Nueva visita con recargo de $ 150.5", detalle);

        // El contrato para Envíos: la versión 1 conserva sus reglas y la vigente trae las nuevas.
        using var scope = factory.Services.CreateScope();
        var administracion = scope.ServiceProvider.GetRequiredService<IAdministracionModuleApi>();
        var vigentes = (await administracion.ReglasDeEntregaVigentesAsync(CancellationToken.None))!;
        Assert.Equal((2, true, PoliticaPendientes.NuevaVisitaConRecargo, 150.50m),
            (vigentes.Numero, vigentes.AdmiteEntregaParcial, vigentes.Pendientes, vigentes.RecargoNuevaVisita));

        // Envíos: el envío nuevo admite la parcial; el anterior sigue con todo o nada (letra 6.6, escenario 5).
        var nuevo = await CrearEnvioAsync();
        var envios = scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>();
        Assert.True((await envios.ConsultarParaVisitaAsync(nuevo, CancellationToken.None))!.AdmiteEntregaParcial);
        Assert.False((await envios.ConsultarParaVisitaAsync(anterior, CancellationToken.None))!.AdmiteEntregaParcial);
    }

    // Los datos iniciales: ninguno admite la parcial y cada operador tiene su política de pendientes (ADR-0002, 2.8).
    [Fact]
    public async Task Los_datos_iniciales_no_admiten_la_parcial_y_cada_operador_tiene_su_politica()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        using var este = await AdminAsync("admin@envios-del-este.uy");

        var deDemo = await TextoAsync(await demo.GetAsync($"{Pagina}/1"));
        var deEste = await TextoAsync(await este.GetAsync($"{Pagina}/1"));

        Assert.Contains("No se admite: todos los bultos o ninguno", deDemo);
        Assert.Contains("Nueva visita sin recargo", deDemo);
        Assert.Contains("No se admite: todos los bultos o ninguno", deEste);
        Assert.Contains("Nueva visita con recargo de $ 150", deEste);
    }

    // El mismo host que crea los envíos: sin Valkey, cada host tiene su propia caché de reglas (ADR-0006) y publicar en
    // otro no invalidaría la de éste. Con sesión, el inquilino sale de la sesión y no del provisorio (TenantDeSesion).
    private HttpClient Cliente() => factory.CreateClient(Sesion.Opciones);

    private async Task<HttpClient> AdminAsync(string email)
    {
        var client = Cliente();
        await Sesion.IngresarAsync(client, email);
        return client;
    }

    // Un envío de Demo con el inquilino provisorio, como las demás pruebas de la API de envíos.
    private async Task<Guid> CrearEnvioAsync()
    {
        using var client = factory.CreateClient();
        var respuesta = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", null, null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10), new BultoRequest(3, 40, 30, 20)]));
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!.Id;
    }

    // Los valores de la versión 1 de Demo, con la entrega parcial prendida y la política indicada.
    private static List<KeyValuePair<string, string>> Valores(string pendientes, string recargo) =>
    [
        new("MaxIntentos", "3"), new("PlazoEntreIntentosHoras", "24"), new("PlazoEstandarHoras", "72"),
        new("PlazoUrgenteHoras", "24"), new("AnticipacionReprogramacionHoras", "24"), new("MaxParadasPorRuta", "20"),
        new("PlazoDevolucionDias", "5"), new("DevolverTrasIntentosAgotados", "true"),
        new("AdmiteEntregaParcial", "true"), new("Pendientes", pendientes), new("RecargoNuevaVisita", recargo),
    ];

    private static async Task<HttpResponseMessage> AccionAsync(HttpClient client, string pagina, string accion,
        List<KeyValuePair<string, string>> campos)
    {
        campos.Add(new("__RequestVerificationToken", await Sesion.TokenAsync(client, pagina)));
        return await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
    }

    private static async Task<string> TextoAsync(HttpResponseMessage respuesta) =>
        WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
}
