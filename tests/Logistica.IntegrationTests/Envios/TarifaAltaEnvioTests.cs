using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Npgsql;

namespace Logistica.IntegrationTests.Envios;

// Base propia: se publica otra versión para verificar CU-05 y CU-10 juntos, sin alterar otras pruebas.
public class TarifaAltaEnvioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Tarifario = "/backoffice/tarifario";
    private const string Borrador = "/backoffice/tarifario/borrador";

    [Fact]
    public async Task Los_nuevos_envios_aplican_los_ajustes_publicados_y_los_anteriores_conservan_su_tarifa()
    {
        using var comercio = factory.CreateClient();
        var anterior = await CrearAsync(comercio);
        Assert.Equal((300m, 1), await TarifaAsync(anterior.Id));

        using var admin = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");
        await AccionAsync(admin, Tarifario, "Nueva", []);
        await AccionAsync(admin, Borrador, "Ajuste", new()
        {
            ["TipoAjuste"] = "Recargo", ["EsPorcentaje"] = "true",
            ["ValorAjuste"] = "10", ["ModalidadAjuste"] = "Estandar",
        });
        await AccionAsync(admin, Borrador, "Ajuste", new()
        {
            ["TipoAjuste"] = "Bonificacion", ["EsPorcentaje"] = "false",
            ["ValorAjuste"] = "5", ["ModalidadAjuste"] = "Estandar",
        });
        await AccionAsync(admin, Borrador, "Ajuste", new()
        {
            ["TipoAjuste"] = "Recargo", ["EsPorcentaje"] = "true",
            ["ValorAjuste"] = "30", ["ModalidadAjuste"] = "Urgente",
        });
        await AccionAsync(admin, Borrador, "Publicar", []);

        // Por bulto: 150 + 10 % - 5 = 160. El anterior sigue en 150 por bulto, versión 1.
        var nuevo = await CrearAsync(comercio);
        Assert.Equal((320m, 2), await TarifaAsync(nuevo.Id));
        Assert.Equal((300m, 1), await TarifaAsync(anterior.Id));

        var urgente = await comercio.PostAsJsonAsync("/api/envios", Request() with { Modalidad = "Urgente" });
        Assert.Equal(HttpStatusCode.Created, urgente.StatusCode);
        var envioUrgente = (await urgente.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        // Urgente: 250 + 30 % = 325 por bulto, sin los ajustes de Estándar.
        Assert.Equal((650m, 2), await TarifaAsync(envioUrgente.Id));

        // Una tercera versión sin reglas de Estándar rechaza el alta; nunca reemplaza el precio por cero.
        await AccionAsync(admin, Tarifario, "Nueva", []);
        var html = WebUtility.HtmlDecode(await admin.GetStringAsync(Borrador));
        var filas = Regex.Matches(html, @"<tr>.*?</tr>", RegexOptions.Singleline);
        foreach (Match fila in filas)
        {
            if (!fila.Value.Contains("Estándar")) continue;
            var regla = Regex.Match(fila.Value, @"id=([0-9a-f-]{36}).*?handler=QuitarRegla|handler=QuitarRegla.*?id=([0-9a-f-]{36})", RegexOptions.Singleline);
            if (!regla.Success) continue;
            var id = regla.Groups[1].Success ? regla.Groups[1].Value : regla.Groups[2].Value;
            await AccionAsync(admin, Borrador, "QuitarRegla", new() { ["id"] = id });
        }
        await AccionAsync(admin, Borrador, "Publicar", new() { ["ConfirmaSinTarifa"] = "true" });
        var rechazo = await comercio.PostAsJsonAsync("/api/envios", Request());
        Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
        Assert.Contains("no tiene una tarifa aplicable", await rechazo.Content.ReadAsStringAsync());
        Assert.Equal((300m, 1), await TarifaAsync(anterior.Id));
    }

    private static CrearEnvioRequest Request() => new(
        new DestinatarioRequest("Ana Pérez", "099123456", null, null),
        new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
        [new(2, 30, 20, 10), new(2, 30, 20, 10)]);

    private static async Task<CrearEnvioResponse> CrearAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/envios", Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
    }

    private static async Task AccionAsync(HttpClient client, string pagina, string accion, Dictionary<string, string> campos)
    {
        campos["__RequestVerificationToken"] = await Sesion.TokenAsync(client, pagina);
        var response = await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<(decimal Monto, int Version)> TarifaAsync(Guid id)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""SELECT "MontoTarifa", "VersionTarifarioNumero" FROM envios."Envios" WHERE "Id" = @id""", conexion);
        comando.Parameters.AddWithValue("id", id);
        await using var reader = await comando.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetDecimal(0), reader.GetInt32(1));
    }
}
