using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Cuenta;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Identidad;

// CU-03 de punta a punta con una sesión real y sin el respaldo provisorio de Development: el operador sale sólo de
// la cookie. ValidationInterval = 0 hace que Identity rearme la cookie desde la base en cada request (normalmente,
// cada 30 minutos), que es cuando se perdía el operador elegido.
public class SeleccionarOperadorTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory.WithWebHostBuilder(b => b
            .UseSetting("Identity:PermitirTenantProvisorio", "false")
            .ConfigureServices(s => s.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero)))
        .CreateClient(Sesion.Opciones);

    [Fact]
    public async Task El_comercio_elige_operador_y_crea_un_envio_aunque_la_cookie_se_rearme()
    {
        using var client = Cliente();

        var ingreso = await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        Assert.Equal("/cuenta/operador?ReturnUrl=%2Fportal%2F", ingreso.Headers.Location!.ToString());

        var eleccion = await Sesion.ElegirOperadorAsync(client, DatosIniciales.OperadorEsteId);
        Assert.Equal("/portal/", eleccion.Headers.Location!.ToString());

        // Este request ya llega con la cookie rearmada: sin conservar el operador, el envío no tendría inquilino.
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", null, null),
            new DireccionRequest("Av. Giannattasio", "1500", "Ciudad de la Costa", "Canelones", "15000", null),
            [new BultoRequest(2, 30, 20, 10)]));

        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var envio = await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>();
        Assert.Equal(DatosIniciales.OperadorEsteId, await OperadorDelEnvioAsync(envio!.Id));
    }

    [Fact]
    public async Task Un_operador_que_no_es_del_comercio_no_se_puede_elegir()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");

        var eleccion = await Sesion.ElegirOperadorAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, eleccion.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_elegir_operador_lleva_al_ingreso_del_Portal()
    {
        using var client = Cliente();

        var respuesta = await client.GetAsync("/cuenta/operador");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("/portal/ingresar", respuesta.Headers.Location!.ToString());
    }

    [Fact]
    public async Task La_sesion_del_portal_muestra_el_usuario_autenticado_y_no_se_guarda_en_cache()
    {
        using var client = Cliente();
        using var anonima = await client.GetAsync("/api/portal/sesion");
        Assert.Equal(HttpStatusCode.Unauthorized, anonima.StatusCode);

        await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(client, DatosIniciales.OperadorDemoId);
        using var respuesta = await client.GetAsync("/api/portal/sesion");
        respuesta.EnsureSuccessStatusCode();
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
        Assert.Equal("comercio@tiendademo.uy",
            (await respuesta.Content.ReadFromJsonAsync<SesionPortalResponse>())!.NombreUsuario);

        using var personal = Cliente();
        await Sesion.IngresarAsync(personal, "repartidor@operador-demo.uy");
        using var ajena = await personal.GetAsync("/api/portal/sesion");
        Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
    }

    // Lee directo de la base, sin pasar por el filtro, para comprobar con qué operador quedó guardado.
    private async Task<Guid> OperadorDelEnvioAsync(Guid envioId)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""SELECT "OperadorId" FROM envios."Envios" WHERE "Id" = @id""", conexion);
        comando.Parameters.AddWithValue("id", envioId);
        return (Guid)(await comando.ExecuteScalarAsync())!;
    }
}
