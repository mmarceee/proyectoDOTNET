using System.Net;
using System.Text.RegularExpressions;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Identidad;

// Administrador general de punta a punta, con sesiones reales: alta de un operador con su primer administrador
// (invitación por el outbox con el operador nuevo), suspensión y activación, y administradores adicionales.
// Crea operadores, así que tiene su propia base.
public class GestionarOperadoresTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string AdminGeneral = "admin@plataforma.uy";
    private const string NuevaContrasena = "Nueva-2026!";

    private WebApplicationFactory<Program> App() => factory.WithWebHostBuilder(b => b
        .UseSetting("Identity:PermitirTenantProvisorio", "false")
        .ConfigureServices(s => s.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero)));

    [Fact]
    public async Task Alta_de_operador_con_su_administrador_de_punta_a_punta()
    {
        var app = App();
        using var general = await IngresarAsync(app, AdminGeneral);

        var (operadorId, enlace) = await CrearOperadorAsync(general, "Envíos del Sur", "envios-del-sur", "admin@envios-del-sur.uy");

        // El operador figura en el listado y su administrador queda pendiente de activación.
        Assert.Contains("Envíos del Sur", await TextoAsync(await general.GetAsync("/backoffice/plataforma/operadores")));
        var detalle = await TextoAsync(await general.GetAsync($"/backoffice/plataforma/operadores/{operadorId}"));
        Assert.Contains("admin@envios-del-sur.uy", detalle);
        Assert.Contains("Pendiente de activación", detalle);

        // La invitación está en el outbox de Administración, con el operador nuevo como inquilino del sobre.
        Assert.Equal(operadorId, await ConsultarAsync<Guid>(
            """SELECT "OperadorId" FROM administracion.outbox_messages WHERE "Tipo" = 'UsuarioInvitado.v1' AND "Contenido"::jsonb ->> 'Email' = @valor""",
            "admin@envios-del-sur.uy"));

        // El administrador activa su cuenta y entra a su operador, vacío: no ve el personal de ningún otro.
        using var nuevo = app.CreateClient(Sesion.Opciones);
        Assert.Contains("Tu cuenta está activa", await ActivarAsync(nuevo, enlace));
        await Sesion.IngresarAsync(nuevo, "admin@envios-del-sur.uy", NuevaContrasena);
        var usuarios = await TextoAsync(await nuevo.GetAsync("/backoffice/usuarios"));
        Assert.Contains("admin@envios-del-sur.uy", usuarios);
        Assert.DoesNotContain("operador-demo.uy", usuarios);
    }

    [Fact]
    public async Task Un_slug_o_un_correo_repetidos_no_crean_nada()
    {
        using var general = await IngresarAsync(App(), AdminGeneral);

        var slug = await TextoAsync(await NuevoOperadorAsync(general, "Otro Demo", "operador-demo", "otro@demo.uy"));
        var correo = await TextoAsync(await NuevoOperadorAsync(general, "Otro", "otro-operador", "admin@operador-demo.uy"));
        var formato = await TextoAsync(await NuevoOperadorAsync(general, "Otro", "Con Espacios", "otro2@demo.uy"));

        Assert.Contains("Ya existe un operador con ese slug.", slug);
        Assert.Contains("Ya existe un usuario con ese correo.", correo);
        Assert.Contains("minúsculas, números y guiones", formato);
        Assert.Equal(0L, await ConsultarAsync<long>(
            """SELECT count(*) FROM administracion."Operadores" WHERE "Slug" = @valor""", "otro-operador"));
    }

    [Fact]
    public async Task Suspender_impide_iniciar_sesion_al_personal_y_activar_lo_devuelve()
    {
        var app = App();
        using var general = await IngresarAsync(app, AdminGeneral);
        var (operadorId, enlace) = await CrearOperadorAsync(general, "Envíos del Norte", "envios-del-norte", "admin@envios-del-norte.uy");
        using var nuevo = app.CreateClient(Sesion.Opciones);
        await ActivarAsync(nuevo, enlace);

        Assert.Equal(HttpStatusCode.Redirect, (await AccionAsync(general, operadorId, "Suspender", [])).StatusCode);
        var ingreso = await Sesion.EnviarFormularioAsync(nuevo, "/cuenta/ingresar",
            new() { ["Email"] = "admin@envios-del-norte.uy", ["Contrasena"] = NuevaContrasena });
        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        Assert.Contains("El correo o la contraseña no son correctos.", await TextoAsync(ingreso));

        Assert.Equal(HttpStatusCode.Redirect, (await AccionAsync(general, operadorId, "Activar", [])).StatusCode);
        await Sesion.IngresarAsync(nuevo, "admin@envios-del-norte.uy", NuevaContrasena);
    }

    [Fact]
    public async Task El_administrador_general_agrega_y_desactiva_administradores()
    {
        using var general = await IngresarAsync(App(), AdminGeneral);
        var (operadorId, _) = await CrearOperadorAsync(general, "Envíos del Oeste", "envios-del-oeste", "uno@envios-del-oeste.uy");

        var agregar = await AccionAsync(general, operadorId, "Administrador",
            new() { ["NombreAdministrador"] = "Segundo", ["EmailAdministrador"] = "dos@envios-del-oeste.uy" });
        Assert.Equal(HttpStatusCode.Redirect, agregar.StatusCode);

        var segundo = await IdDeAsync("dos@envios-del-oeste.uy");
        var desactivar = await AccionAsync(general, operadorId, "Desactivar", new() { ["UsuarioId"] = segundo.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, desactivar.StatusCode);
        Assert.Contains("Desactivado", await TextoAsync(await general.GetAsync($"/backoffice/plataforma/operadores/{operadorId}")));

        // Un usuario de otro operador no es un administrador de éste: 404.
        var ajeno = await AccionAsync(general, operadorId, "Desactivar",
            new() { ["UsuarioId"] = (await IdDeAsync("admin@operador-demo.uy")).ToString() });
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
    }

    [Fact]
    public async Task Solo_el_administrador_general_gestiona_operadores_y_no_ve_datos_de_ningun_operador()
    {
        var app = App();
        using var adminDemo = await IngresarAsync(app, "admin@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await adminDemo.GetAsync("/backoffice/plataforma/operadores")).StatusCode);

        using var general = await IngresarAsync(app, AdminGeneral);
        Assert.Contains("/backoffice/plataforma/operadores", await TextoAsync(await general.GetAsync("/backoffice")));
        Assert.Equal(HttpStatusCode.Forbidden, (await general.GetAsync("/backoffice/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await general.GetAsync("/backoffice/zonas")).StatusCode);
    }

    private static async Task<HttpClient> IngresarAsync(WebApplicationFactory<Program> app, string email)
    {
        var client = app.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, email);
        return client;
    }

    private static Task<HttpResponseMessage> NuevoOperadorAsync(HttpClient client, string nombre, string slug, string email) =>
        Sesion.EnviarFormularioAsync(client, "/backoffice/plataforma/operadores/nuevo", new()
        {
            ["Nombre"] = nombre, ["Slug"] = slug, ["ZonaHoraria"] = "America/Montevideo",
            ["NombreAdministrador"] = $"Administración {nombre}", ["EmailAdministrador"] = email,
        });

    // Crea el operador y devuelve su id y el enlace de activación de su administrador (en Development, el detalle lo muestra).
    private static async Task<(Guid OperadorId, string Enlace)> CrearOperadorAsync(
        HttpClient client, string nombre, string slug, string email)
    {
        var alta = await NuevoOperadorAsync(client, nombre, slug, email);
        Assert.Equal(HttpStatusCode.Redirect, alta.StatusCode);
        var ubicacion = alta.Headers.Location!.ToString();
        var detalle = await TextoAsync(await client.GetAsync(ubicacion));
        var enlace = Regex.Match(detalle, "href=\"(/cuenta/activar[^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(enlace);
        return (Guid.Parse(ubicacion[(ubicacion.LastIndexOf('/') + 1)..]), enlace);
    }

    private static async Task<HttpResponseMessage> AccionAsync(
        HttpClient client, Guid operadorId, string accion, Dictionary<string, string> campos)
    {
        var pagina = $"/backoffice/plataforma/operadores/{operadorId}";
        campos["__RequestVerificationToken"] = await Sesion.TokenAsync(client, pagina);
        return await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
    }

    private static async Task<string> ActivarAsync(HttpClient client, string enlace)
    {
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(enlace));
        var campos = new Dictionary<string, string>
        {
            ["Usuario"] = Regex.Match(html, "name=\"Usuario\"[^>]*value=\"([^\"]+)\"").Groups[1].Value,
            ["Token"] = Regex.Match(html, "name=\"Token\"[^>]*value=\"([^\"]+)\"").Groups[1].Value,
            ["Contrasena"] = NuevaContrasena,
            ["Confirmacion"] = NuevaContrasena,
            ["__RequestVerificationToken"] = await Sesion.TokenAsync(client, enlace),
        };
        return await TextoAsync(await client.PostAsync("/cuenta/activar", new FormUrlEncodedContent(campos)));
    }

    private static async Task<string> TextoAsync(HttpResponseMessage respuesta) =>
        WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

    private async Task<Guid> IdDeAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByEmailAsync(email))!.Id;
    }

    private async Task<T> ConsultarAsync<T>(string sql, string valor)
    {
        await using var db = new NpgsqlConnection(factory.ConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("valor", valor);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
