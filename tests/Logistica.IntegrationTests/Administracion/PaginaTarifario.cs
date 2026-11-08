using System.Net;
using System.Text.RegularExpressions;
using Logistica.IntegrationTests.Identidad;

namespace Logistica.IntegrationTests.Administracion;

// Las acciones de la pantalla del tarifario (CU-05) que usan las pruebas: armar el borrador, publicarlo y calcular un
// precio con el simulador del listado.
internal static class PaginaTarifario
{
    public const string Pagina = "/backoffice/tarifario";
    public const string Borrador = "/backoffice/tarifario/borrador";
    public const string ZonaDemo = "61111111-1111-1111-1111-111111111111";
    public const string ZonaEste = "63333333-3333-3333-3333-333333333333";

    public static List<KeyValuePair<string, string>> Regla(string zona, string modalidad, string pesoDesde,
        string pesoHasta, string volumenDesde, string volumenHasta, string precio) =>
    [
        new("ZonaId", zona), new("Modalidad", modalidad), new("PesoDesde", pesoDesde), new("PesoHasta", pesoHasta),
        new("VolumenDesde", volumenDesde), new("VolumenHasta", volumenHasta), new("Precio", precio),
    ];

    // Un bulto de 2 kg y 0,01 m³ con el simulador del listado, que usa la misma lectura con caché que el alta de envíos.
    public static async Task<string> CalcularAsync(HttpClient client, string zona, string modalidad)
    {
        return await TextoAsync(await AccionAsync(client, Pagina, "Calcular",
            [new("ZonaId", zona), new("Modalidad", modalidad), new("PesoKg", "2"), new("VolumenM3", "0.01")]));
    }

    // Quita del borrador la regla cuyo precio se muestra con ese texto.
    public static async Task QuitarReglaAsync(HttpClient client, string precio)
    {
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(Borrador));
        var fila = Regex.Match(html, Regex.Escape(precio) + "</td>.*?id=([0-9a-f-]{36})", RegexOptions.Singleline);
        Assert.True(fila.Success, $"No se encontró la regla de {precio}.");

        var token = await Sesion.TokenAsync(client, Borrador);
        await client.PostAsync($"{Borrador}?handler=QuitarRegla&id={fila.Groups[1].Value}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
    }

    // Una acción de la página (?handler=...), con el token antifalsificación de la página.
    public static async Task<HttpResponseMessage> AccionAsync(HttpClient client, string pagina, string accion,
        List<KeyValuePair<string, string>> campos)
    {
        campos.Add(new("__RequestVerificationToken", await Sesion.TokenAsync(client, pagina)));
        return await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
    }

    // Razor codifica las tildes: se decodifica para buscar el texto que ve el usuario.
    public static async Task<string> TextoAsync(HttpResponseMessage respuesta)
    {
        return WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
    }
}
