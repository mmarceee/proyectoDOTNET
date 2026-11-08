using System.Net;
using System.Text.RegularExpressions;
using Logistica.IntegrationTests.Identidad;
using Microsoft.AspNetCore.Hosting;
using static Logistica.IntegrationTests.Administracion.PaginaTarifario;

namespace Logistica.IntegrationTests.Administracion;

// CU-05 de punta a punta, con sesiones reales y sin el respaldo provisorio. Cambia el tarifario de los dos operadores,
// así que tiene su propia base. El recorrido del borrador de Demo va en una sola prueba, en orden, para que las demás
// no dependan de lo que quedó en el borrador.
public class ConfigurarTarifarioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    // Datos iniciales y aislamiento (ADR-0002): cada operador ve su versión 1 con sus zonas; la de otro no aparece.
    [Fact]
    public async Task Cada_operador_ve_su_version_vigente_y_no_la_del_otro()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        using var este = await AdminAsync("admin@envios-del-este.uy");

        var deDemo = await TextoAsync(await demo.GetAsync($"{Pagina}/1"));
        var deEste = await TextoAsync(await este.GetAsync($"{Pagina}/1"));

        Assert.Contains("MVD-DEMO", deDemo);
        Assert.DoesNotContain("CDC-ESTE", deDemo);
        Assert.Contains("CDC-ESTE", deEste);
        Assert.Contains("30 %", deEste);
    }

    [Fact]
    public async Task Solo_el_administrador_configura_el_tarifario()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Pagina)).StatusCode);
    }

    // Flujo principal, A1 a A4 y el escenario 5: un cambio de precio publicado desde la pantalla rige enseguida, aunque
    // la versión anterior estuviera en la caché.
    [Fact]
    public async Task Recorrido_completo_del_borrador_hasta_el_precio_nuevo()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");

        // Antes: la versión 1 cobra $150 un bulto Estándar chico. Queda en la caché.
        Assert.Contains("$ 150 con la versión 1", await CalcularAsync(demo, ZonaDemo, "Estandar"));

        // Pasos 3 y 4: el borrador copia las 4 reglas de la vigente. A4: pedir otra versión abre la misma.
        await AccionAsync(demo, Pagina, "Nueva", []);
        await AccionAsync(demo, Pagina, "Nueva", []);
        var listado = await TextoAsync(await demo.GetAsync(Pagina));
        Assert.Single(Regex.Matches(listado, ">Borrador<"));
        Assert.Equal(4, Regex.Matches(await demo.GetStringAsync(Borrador), "handler=QuitarRegla").Count);

        // A1: una regla que se pisa con 0 a 5 kg no deja publicar.
        await AccionAsync(demo, Borrador, "Regla", Regla(ZonaDemo, "Estandar", "3", "8", "0", "0.1", "999"));
        var conflicto = await TextoAsync(await AccionAsync(demo, Borrador, "Publicar", []));
        Assert.Contains("Hay reglas superpuestas", conflicto);
        await QuitarReglaAsync(demo, "$ 999");

        // A3: no se publica hacia atrás.
        var pasado = await TextoAsync(await AccionAsync(demo, Borrador, "Publicar", [new("Fecha", "2020-01-01")]));
        Assert.Contains("La fecha de vigencia no puede estar en el pasado.", pasado);

        // Nuevo precio: 0 a 5 kg Estándar pasa de $150 a $175. Y se quitan las dos reglas de Urgente.
        await QuitarReglaAsync(demo, "$ 150");
        await AccionAsync(demo, Borrador, "Regla", Regla(ZonaDemo, "Estandar", "0", "5", "0", "0.1", "175"));
        await QuitarReglaAsync(demo, "$ 250");
        await QuitarReglaAsync(demo, "$ 450");

        // A2: se advierte que Urgente queda sin precio y no se publica sin confirmar.
        var advertencia = await TextoAsync(await AccionAsync(demo, Borrador, "Publicar", []));
        Assert.Contains("Hay zonas sin precio para alguna modalidad.", advertencia);
        Assert.Contains("MVD-DEMO · Urgente", advertencia);

        var publicada = await AccionAsync(demo, Borrador, "Publicar", [new("ConfirmaSinTarifa", "true")]);
        Assert.Equal(HttpStatusCode.Redirect, publicada.StatusCode);

        // La 2 rige y la 1 quedó reemplazada; el precio nuevo se ve enseguida (se invalidó la caché).
        listado = await TextoAsync(await demo.GetAsync(Pagina));
        Assert.Matches("Versión 2</a>\\s*</td>\\s*<td><span class=\"badge text-bg-success\">Vigente", listado);
        Assert.Matches("Versión 1</a>\\s*</td>\\s*<td><span class=\"badge text-bg-secondary\">Reemplazada", listado);
        Assert.Contains("$ 175 con la versión 2", await CalcularAsync(demo, ZonaDemo, "Estandar"));
        Assert.Contains("Sin precio", await CalcularAsync(demo, ZonaDemo, "Urgente"));

        // Una versión publicada no se modifica: ya no hay borrador para editar y se vuelve al listado. El token se pide al
        // listado, porque la página del borrador ya redirige.
        var campos = Regla(ZonaDemo, "Estandar", "30", "40", "0", "0.1", "500");
        campos.Add(new("__RequestVerificationToken", await Sesion.TokenAsync(demo, Pagina)));
        var sinBorrador = await demo.PostAsync($"{Borrador}?handler=Regla", new FormUrlEncodedContent(campos));
        Assert.Equal(Pagina, sinBorrador.Headers.Location?.ToString());
    }

    // Publicada con fecha futura: queda programada y, hasta ese día, sigue cobrando la anterior.
    [Fact]
    public async Task Una_version_con_fecha_futura_queda_programada_y_sigue_la_anterior()
    {
        using var este = await AdminAsync("admin@envios-del-este.uy");
        await AccionAsync(este, Pagina, "Nueva", []);

        var publicada = await AccionAsync(este, Borrador, "Publicar", [new("Fecha", "2035-01-01")]);
        Assert.Equal(HttpStatusCode.Redirect, publicada.StatusCode);

        var listado = await TextoAsync(await este.GetAsync(Pagina));
        Assert.Contains(">Programada<", listado);
        Assert.Contains("$ 180 con la versión 1", await CalcularAsync(este, ZonaEste, "Estandar"));
    }

    private async Task<HttpClient> AdminAsync(string email)
    {
        var client = Cliente();
        await Sesion.IngresarAsync(client, email);
        return client;
    }
}
