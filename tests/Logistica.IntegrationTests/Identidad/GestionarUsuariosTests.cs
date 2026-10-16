using System.Net;
using System.Text.RegularExpressions;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Identidad;

// CU-02 de punta a punta, con sesiones reales y sin el respaldo provisorio. ValidationInterval = 0: la cookie se
// revalida en cada request, así se ve enseguida que desactivar cierra las sesiones (A4).
// Cambia usuarios del operador Demo, así que tiene su propia base.
public class GestionarUsuariosTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string NuevaContrasena = "Nueva-2026!";

    private WebApplicationFactory<Program> App() => factory.WithWebHostBuilder(b => b
        .UseSetting("Identity:PermitirTenantProvisorio", "false")
        .ConfigureServices(s => s.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero)));

    // Paso 2 y aislamiento (ADR-0002, sección 4): el administrador ve su personal y los usuarios de sus comercios, y
    // ningún usuario de otro operador, aunque Usuario no tenga filtro "Tenant".
    [Fact]
    public async Task El_listado_muestra_el_personal_y_los_comercios_del_operador_y_nada_de_otro()
    {
        using var admin = await AdminDemoAsync();

        var html = await admin.GetStringAsync("/backoffice/usuarios");

        Assert.Contains("despacho@operador-demo.uy", html);
        Assert.Contains("comercio@tiendademo.uy", html);
        Assert.DoesNotContain("despacho@envios-del-este.uy", html);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/backoffice/usuarios/{await IdDeAsync("despacho@envios-del-este.uy")}")).StatusCode);
    }

    [Fact]
    public async Task Solo_el_administrador_gestiona_usuarios()
    {
        using var client = App().CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, "despacho@envios-del-este.uy");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/backoffice/usuarios")).StatusCode);
    }

    // Pasos 3 a 7, y el enlace de un solo uso (requerimiento 3).
    [Fact]
    public async Task Alta_invitacion_y_activacion_de_punta_a_punta()
    {
        var app = App();
        using var admin = app.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");
        const string email = "nueva.despachante@operador-demo.uy";

        var alta = await Sesion.EnviarFormularioAsync(admin, "/backoffice/usuarios/nuevo",
            new() { ["Nombre"] = "Nueva Despachante", ["Email"] = email, ["Perfil"] = "Despachador" });
        Assert.Equal(HttpStatusCode.Redirect, alta.StatusCode);

        // Queda pendiente, y la invitación está en el outbox de Administración.
        var detalle = WebUtility.HtmlDecode(await admin.GetStringAsync(alta.Headers.Location!.ToString()));
        Assert.Contains("Pendiente de activación", detalle);
        Assert.Equal(1L, await ContarAsync(
            """SELECT count(*) FROM administracion.outbox_messages WHERE "Tipo" = 'UsuarioInvitado.v1' AND "Contenido"::jsonb ->> 'Email' = @valor""",
            email));

        // El invitado, sin sesión, define su contraseña con el enlace (en Development, el detalle lo muestra).
        var enlace = Regex.Match(detalle, "href=\"(/cuenta/activar[^\"]+)\"").Groups[1].Value;
        using var invitado = app.CreateClient(Sesion.Opciones);
        var activacion = await ActivarAsync(invitado, enlace);
        Assert.Contains("Tu cuenta está activa", activacion);

        // Ya puede iniciar sesión; el mismo enlace no sirve dos veces.
        await Sesion.IngresarAsync(invitado, email, NuevaContrasena);
        using var otro = app.CreateClient(Sesion.Opciones);
        Assert.Contains("venció o ya se usó", await ActivarAsync(otro, enlace));
    }

    // A1: correo ya registrado.
    [Fact]
    public async Task Un_correo_ya_registrado_no_se_vuelve_a_dar_de_alta()
    {
        using var admin = await AdminDemoAsync();

        var html = await TextoAsync(await Sesion.EnviarFormularioAsync(admin, "/backoffice/usuarios/nuevo",
            new() { ["Nombre"] = "Otro", ["Email"] = "despacho@envios-del-este.uy", ["Perfil"] = "Despachador" }));

        Assert.Contains("Ya existe un usuario con ese correo.", html);
    }

    // Precondición 2: el usuario de comercio, sólo de un comercio con relación activa con el operador.
    [Fact]
    public async Task Un_usuario_de_comercio_necesita_un_comercio_del_operador()
    {
        using var admin = await AdminDemoAsync();

        var html = await TextoAsync(await Sesion.EnviarFormularioAsync(admin, "/backoffice/usuarios/nuevo",
            new()
            {
                ["Nombre"] = "Tienda ajena", ["Email"] = "ajena@comercio.uy", ["Perfil"] = "UsuarioComercio",
                ["ComercioId"] = Guid.NewGuid().ToString(),
            }));

        Assert.Contains("relación activa", html);
    }

    // A3.
    [Fact]
    public async Task El_administrador_cambia_el_perfil_del_personal()
    {
        using var admin = await AdminDemoAsync();
        var id = await IdDeAsync("despacho@operador-demo.uy");

        var respuesta = await AccionAsync(admin, id, "Perfil", new() { ["NuevoPerfil"] = "OperarioDeposito" });

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(["OperarioDeposito"], await PerfilesDeAsync("despacho@operador-demo.uy"));
    }

    // A4: desactivar cierra las sesiones abiertas.
    [Fact]
    public async Task Desactivar_cierra_las_sesiones_abiertas_del_usuario()
    {
        var app = App();
        using var deposito = app.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        Assert.Equal(HttpStatusCode.OK, (await deposito.GetAsync("/backoffice/envios")).StatusCode);

        using var admin = app.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");
        var respuesta = await AccionAsync(admin, await IdDeAsync("deposito@operador-demo.uy"), "Desactivar", []);
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);

        // Su cookie ya no vale: el Backoffice lo manda a iniciar sesión.
        Assert.NotEqual(HttpStatusCode.OK, (await deposito.GetAsync("/backoffice/envios")).StatusCode);
    }

    // A5: ni desactivarse a sí mismo, ni dejar al operador sin administradores.
    [Fact]
    public async Task El_operador_no_se_queda_sin_administradores()
    {
        using var admin = await AdminDemoAsync();
        var propio = await IdDeAsync("admin@operador-demo.uy");

        var desactivarse = await TextoAsync(await AccionAsync(admin, propio, "Desactivar", []));
        var dejarDeSerAdmin = await TextoAsync(await AccionAsync(admin, propio, "Perfil", new() { ["NuevoPerfil"] = "Despachador" }));

        Assert.Contains("No podés desactivar tu propio usuario.", desactivarse);
        Assert.Contains("al menos un administrador activo", dejarDeSerAdmin);
    }

    // A4: al usuario de comercio no se lo desactiva.
    [Fact]
    public async Task Al_usuario_de_comercio_no_se_lo_desactiva()
    {
        using var admin = await AdminDemoAsync();

        var html = await TextoAsync(await AccionAsync(admin, await IdDeAsync("comercio@tiendademo.uy"), "Desactivar", []));

        Assert.Contains("se suspende su relación comercial", html);
    }

    private async Task<HttpClient> AdminDemoAsync()
    {
        var client = App().CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, "admin@operador-demo.uy");
        return client;
    }

    // Una acción del detalle (?handler=...), con el token antifalsificación de la página.
    private static async Task<HttpResponseMessage> AccionAsync(
        HttpClient client, Guid usuarioId, string accion, Dictionary<string, string> campos)
    {
        var pagina = $"/backoffice/usuarios/{usuarioId}";
        campos["__RequestVerificationToken"] = await Sesion.TokenAsync(client, pagina);
        return await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
    }

    // Abre el enlace de activación y define la contraseña, como el invitado en el navegador.
    private static async Task<string> ActivarAsync(HttpClient client, string enlace)
    {
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(enlace));
        if (html.Contains("venció o ya se usó"))
        {
            return html;
        }

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

    // Razor codifica las tildes: se decodifica para buscar el texto que ve el usuario.
    private static async Task<string> TextoAsync(HttpResponseMessage respuesta)
    {
        return WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
    }

    private async Task<Guid> IdDeAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var usuario = await scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByEmailAsync(email);
        return usuario!.Id;
    }

    private async Task<IList<string>> PerfilesDeAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        return await usuarios.GetRolesAsync((await usuarios.FindByEmailAsync(email))!);
    }

    private async Task<long> ContarAsync(string sql, string valor)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("valor", valor);
        return (long)(await comando.ExecuteScalarAsync())!;
    }
}
