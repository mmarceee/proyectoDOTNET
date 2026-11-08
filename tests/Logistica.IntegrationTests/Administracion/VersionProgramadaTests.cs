using Logistica.IntegrationTests.Identidad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Logistica.IntegrationTests.Administracion;

// ADR-0006, prueba 3: una versión programada empieza a regir sola al llegar su día, sin que nadie publique nada, aunque
// el puntero "vigente" de la caché todavía no venció. Lo resuelve ValidoHasta (TarifarioVigente y ReglasVigentes). El
// reloj de la API se adelanta a mano; la caché sigue contando su minuto con el reloj real. Tiene su propia base porque
// publica versiones.
public class VersionProgramadaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string ZonaDemo = "61111111-1111-1111-1111-111111111111";

    // 31/12/2034 a las 23:59 en Montevideo (UTC-3): la versión del 1/1/2035 empieza en un minuto. Se cruza el instante
    // exacto con dos minutos de reloj, así la sesión del administrador sigue vigente.
    private static readonly DateTimeOffset VisperaDelCambio = new(2035, 1, 1, 2, 59, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DespuesDelCambio = new(2035, 1, 1, 3, 1, 0, TimeSpan.Zero);

    [Fact]
    public async Task El_tarifario_programado_rige_al_llegar_su_dia_sin_esperar_a_que_venza_la_cache()
    {
        var reloj = new RelojAjustable(VisperaDelCambio);
        using var demo = await AdminAsync(reloj);
        await AccionAsync(demo, "/backoffice/tarifario", "Nueva", []);
        await AccionAsync(demo, "/backoffice/tarifario/borrador", "Publicar", [new("Fecha", "2035-01-01")]);

        // La víspera rige la versión 1, y el puntero queda guardado en la caché.
        Assert.Contains("con la versión 1", await CalcularAsync(demo));

        // A las 00:01, sin publicar nada, ya rige la versión 2.
        reloj.Ahora = DespuesDelCambio;
        Assert.Contains("con la versión 2", await CalcularAsync(demo));
    }

    [Fact]
    public async Task Las_reglas_programadas_rigen_al_llegar_su_dia_sin_esperar_a_que_venza_la_cache()
    {
        var reloj = new RelojAjustable(VisperaDelCambio);
        using var demo = await AdminAsync(reloj);
        await AccionAsync(demo, "/backoffice/reglas", "Nueva", []);
        await AccionAsync(demo, "/backoffice/reglas/borrador", "Publicar", [new("Fecha", "2035-01-01")]);

        Assert.Contains("Rigen ahora: versión 1", await TextoAsync(await demo.GetAsync("/backoffice/reglas")));

        reloj.Ahora = DespuesDelCambio;
        Assert.Contains("Rigen ahora: versión 2", await TextoAsync(await demo.GetAsync("/backoffice/reglas")));
    }

    // Una API con el reloj de la prueba, y la sesión del administrador de Demo.
    private async Task<HttpClient> AdminAsync(RelojAjustable reloj)
    {
        var client = factory.WithWebHostBuilder(b => b
                .UseSetting("Identity:PermitirTenantProvisorio", "false")
                .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(reloj)))
            .CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, "admin@operador-demo.uy");
        return client;
    }

    // Un bulto de 2 kg y 0,01 m³ con el simulador del tarifario, que usa la misma lectura con caché que el alta de envíos.
    private static async Task<string> CalcularAsync(HttpClient client) =>
        await TextoAsync(await AccionAsync(client, "/backoffice/tarifario", "Calcular",
            [new("ZonaId", ZonaDemo), new("Modalidad", "Estandar"), new("PesoKg", "2"), new("VolumenM3", "0.01")]));

    private static async Task<HttpResponseMessage> AccionAsync(HttpClient client, string pagina, string accion,
        List<KeyValuePair<string, string>> campos)
    {
        campos.Add(new("__RequestVerificationToken", await Sesion.TokenAsync(client, pagina)));
        return await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
    }

    private static async Task<string> TextoAsync(HttpResponseMessage respuesta) =>
        WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

    // Un reloj que la prueba adelanta cuando quiere.
    private sealed class RelojAjustable(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = inicio;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }
}
