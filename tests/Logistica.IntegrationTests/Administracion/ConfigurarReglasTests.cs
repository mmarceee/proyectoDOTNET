using System.Net;
using System.Text.RegularExpressions;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Administracion;

// CU-06 de punta a punta, con sesiones reales y sin el respaldo provisorio. Cambia las reglas de los dos operadores, así
// que tiene su propia base. El recorrido del borrador de Demo va en una sola prueba, en orden.
public class ConfigurarReglasTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Pagina = "/backoffice/reglas";
    private const string Borrador = "/backoffice/reglas/borrador";

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    // Datos iniciales distintos y aislamiento: Este exige foto, Demo no, y cada uno ve sólo su versión. Se mira la
    // versión 1, que no cambia aunque otra prueba publique.
    [Fact]
    public async Task Cada_operador_ve_sus_reglas_y_no_las_del_otro()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        using var este = await AdminAsync("admin@envios-del-este.uy");

        var deDemo = await TextoAsync(await demo.GetAsync($"{Pagina}/1"));
        var deEste = await TextoAsync(await este.GetAsync($"{Pagina}/1"));

        Assert.Matches("Máximo de intentos</dt><dd class=\"col-sm-8\">3<", deDemo);
        Assert.Matches("Pruebas exigidas</dt>\\s*<dd class=\"col-sm-8\">Firma</dd>", deDemo);
        Assert.Matches("Máximo de intentos</dt><dd class=\"col-sm-8\">2<", deEste);
        Assert.Contains("Firma, foto, nombre y documento", deEste);
    }

    [Fact]
    public async Task Solo_el_administrador_configura_las_reglas()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Pagina)).StatusCode);
    }

    // Flujo principal, A1, A2 y el escenario 5: "desde ahora se exige foto" rige enseguida para los envíos nuevos, y un
    // envío admitido con la versión 1 conserva sus reglas (letra 6.6).
    [Fact]
    public async Task Recorrido_completo_hasta_exigir_foto_desde_ahora()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        var version1 = await IdDeVersionAsync(1);

        await AccionAsync(demo, Pagina, "Nueva", []);
        var borrador = await TextoAsync(await demo.GetAsync(Borrador));
        Assert.Equal(4, Regex.Matches(borrador, "handler=QuitarMotivo").Count);

        // A1: valores inválidos se muestran todos y no dejan publicar.
        await AccionAsync(demo, Borrador, "Valores", Valores(maxIntentos: "0", urgente: "0", exigeFoto: false));
        var invalido = await TextoAsync(await AccionAsync(demo, Borrador, "Publicar", []));
        Assert.Contains("La cantidad máxima de intentos tiene que ser al menos 1.", invalido);
        Assert.Contains("Falta un plazo mayor que cero para la modalidad Urgente.", invalido);

        // A2: sin motivos no se publica.
        await AccionAsync(demo, Borrador, "Valores", Valores(exigeFoto: true));
        foreach (var id in await MotivosAsync(demo))
        {
            await QuitarMotivoAsync(demo, id);
        }

        var sinMotivos = await TextoAsync(await AccionAsync(demo, Borrador, "Publicar", []));
        Assert.Contains("El catálogo de motivos de no entrega no puede quedar vacío.", sinMotivos);

        await AccionAsync(demo, Borrador, "Motivo",
            [new("Codigo", "ausente"), new("Descripcion", "Destinatario ausente"), new("Reprogramable", "true")]);
        var publicada = await AccionAsync(demo, Borrador, "Publicar", []);
        Assert.Equal(HttpStatusCode.Redirect, publicada.StatusCode);

        // Rige la 2 y exige foto; se ve enseguida porque se invalidó la caché.
        var listado = await TextoAsync(await demo.GetAsync(Pagina));
        Assert.Contains("Rigen ahora: versión 2", listado);
        Assert.Contains("Pruebas: firma, foto", listado);

        // El contrato para Envíos: la versión 1 conserva sus reglas; la vigente es la 2.
        using var scope = factory.Services.CreateScope();
        var administracion = scope.ServiceProvider.GetRequiredService<IAdministracionModuleApi>();
        var reglas1 = await administracion.ReglasDeEntregaAsync(version1, CancellationToken.None);
        var vigentes = await administracion.ReglasDeEntregaVigentesAsync(CancellationToken.None);
        Assert.False(reglas1!.ExigeFoto);
        Assert.Equal(4, reglas1.Motivos.Count);
        Assert.Equal(2, vigentes!.Numero);
        Assert.True(vigentes.ExigeFoto);
        Assert.Equal("AUSENTE", Assert.Single(vigentes.Motivos).Codigo);
    }

    // Acordado con Lucas (19/10): Planificación toma el máximo de paradas y el Id de versión de las reglas que rigen,
    // con el mismo contrato de siempre (ObtenerReglasPlanificacionAsync).
    [Fact]
    public async Task Planificacion_usa_el_maximo_de_paradas_de_las_reglas_vigentes()
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        using var scope = factory.Services.CreateScope();
        var administracion = scope.ServiceProvider.GetRequiredService<IAdministracionModuleApi>();

        var planificacion = await administracion.ObtenerReglasPlanificacionAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        // Se compara con la base y no con ReglasDeEntregaVigentesAsync: esa lectura pasa por la caché, que las demás
        // pruebas de la clase comparten, y otra prueba puede publicar una versión nueva.
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            SELECT "Id" FROM administracion."VersionesReglas"
            WHERE "OperadorId" = @operador AND "Estado" <> 'Borrador' AND "VigenteDesde" <= now()
              AND ("VigenteHasta" IS NULL OR now() < "VigenteHasta")
            ORDER BY "Numero" DESC LIMIT 1
            """, conexion);
        comando.Parameters.AddWithValue("operador", DatosIniciales.OperadorDemoId);

        Assert.Equal((Guid)(await comando.ExecuteScalarAsync())!, planificacion.VersionId);
        Assert.Equal(20, planificacion.MaxParadasPorRuta);
    }

    // Con fecha futura: queda programada y, hasta ese día, siguen las reglas anteriores.
    [Fact]
    public async Task Unas_reglas_con_fecha_futura_quedan_programadas()
    {
        using var este = await AdminAsync("admin@envios-del-este.uy");
        await AccionAsync(este, Pagina, "Nueva", []);

        var publicada = await AccionAsync(este, Borrador, "Publicar", [new("Fecha", "2035-01-01")]);
        Assert.Equal(HttpStatusCode.Redirect, publicada.StatusCode);

        var listado = await TextoAsync(await este.GetAsync(Pagina));
        Assert.Contains(">Programada<", listado);
        Assert.Contains("Rigen ahora: versión 1", listado);
    }

    private async Task<HttpClient> AdminAsync(string email)
    {
        var client = Cliente();
        await Sesion.IngresarAsync(client, email);
        return client;
    }

    // Los valores iniciales de Demo, con los cambios que pide cada paso.
    private static List<KeyValuePair<string, string>> Valores(string maxIntentos = "3", string urgente = "24",
        bool exigeFoto = false)
    {
        var campos = new List<KeyValuePair<string, string>>
        {
            new("MaxIntentos", maxIntentos), new("PlazoEntreIntentosHoras", "24"), new("PlazoEstandarHoras", "72"),
            new("PlazoUrgenteHoras", urgente), new("AnticipacionReprogramacionHoras", "24"), new("MaxParadasPorRuta", "20"),
            new("PlazoDevolucionDias", "5"), new("DevolverTrasIntentosAgotados", "true"),
        };
        if (exigeFoto)
        {
            campos.Add(new("ExigeFoto", "true"));
        }

        return campos;
    }

    private static async Task<IReadOnlyList<string>> MotivosAsync(HttpClient client)
    {
        // El orden de los parámetros del enlace puede variar: se toma el Id de cada acción de quitar.
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(Borrador));
        return Regex.Matches(html, "action=\"([^\"]*QuitarMotivo[^\"]*)\"")
            .Select(m => Regex.Match(m.Groups[1].Value, "id=([0-9a-f-]{36})").Groups[1].Value).ToList();
    }

    private static async Task QuitarMotivoAsync(HttpClient client, string id)
    {
        var token = await Sesion.TokenAsync(client, Borrador);
        await client.PostAsync($"{Borrador}?handler=QuitarMotivo&id={id}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
    }

    // El Id de una versión de reglas de Demo, leído directo de la base.
    private async Task<Guid> IdDeVersionAsync(int numero)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(
            """SELECT "Id" FROM administracion."VersionesReglas" WHERE "OperadorId" = @operador AND "Numero" = @numero""", conexion);
        comando.Parameters.AddWithValue("operador", DatosIniciales.OperadorDemoId);
        comando.Parameters.AddWithValue("numero", numero);
        return (Guid)(await comando.ExecuteScalarAsync())!;
    }

    // Una acción de la página (?handler=...), con el token antifalsificación de la página.
    private static async Task<HttpResponseMessage> AccionAsync(HttpClient client, string pagina, string accion,
        List<KeyValuePair<string, string>> campos)
    {
        campos.Add(new("__RequestVerificationToken", await Sesion.TokenAsync(client, pagina)));
        return await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
    }

    private static async Task<string> TextoAsync(HttpResponseMessage respuesta)
    {
        return WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
    }
}
