using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Envios;

// CU-20 de punta a punta: el despachador desde el Backoffice y el comercio desde la API, con sesión real y el respaldo
// provisorio apagado.
public class IniciarDevolucionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    // Flujo principal (T12): el envío queda EnDevolucion, con su evento, la Devolucion Pendiente y el mensaje del Outbox.
    [Fact]
    public async Task El_despachador_inicia_la_devolucion_y_queda_todo_registrado()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var respuesta = await IniciarDesdeBackofficeAsync(despacho, envio.Numero, "El cliente canceló la compra");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal($"/backoffice/envios/{envio.Numero}", respuesta.Headers.Location?.OriginalString);
        Assert.Equal("EnDevolucion", await EstadoAsync(envio.Id));
        Assert.Equal("Backoffice", await ConsultarAsync<string>(
            """SELECT "Origen" FROM envios."EventosEnvio" WHERE "EnvioId" = @id AND "EstadoNuevo" = 'EnDevolucion'""", envio.Id));
        Assert.Equal("Pendiente", await ConsultarAsync<string>(
            """SELECT "Estado" FROM envios."Devoluciones" WHERE "EnvioId" = @id AND "Motivo" = 'El cliente canceló la compra'""", envio.Id));
        Assert.Equal(1L, await MensajesAsync(envio.Id));
    }

    // Paso 2: sin motivo no se confirma y no cambia nada.
    [Fact]
    public async Task Sin_motivo_no_cambia_nada()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var respuesta = await IniciarDesdeBackofficeAsync(despacho, envio.Numero, "");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode); // vuelve a mostrar la página con el aviso
        Assert.Equal("EnDeposito", await EstadoAsync(envio.Id));
        Assert.Equal(0L, await MensajesAsync(envio.Id));
    }

    // A1: un envío que todavía no llegó al depósito no se puede devolver; la página muestra el motivo.
    [Fact]
    public async Task Desde_un_estado_no_permitido_se_informa_y_no_cambia()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: false);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var respuesta = await IniciarDesdeBackofficeAsync(despacho, envio.Numero, "No lo quieren");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("no puede pasar de Admitido a EnDevolucion", WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
        Assert.Equal("Admitido", await EstadoAsync(envio.Id));
        Assert.Equal(0L, await MensajesAsync(envio.Id));
    }

    // El comercio la pide por la API con el envío en el depósito (T12).
    [Fact]
    public async Task El_comercio_inicia_la_devolucion_por_la_api()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var comercio = await ComercioAsync(DatosIniciales.OperadorDemoId);

        var respuesta = await comercio.PostAsJsonAsync($"/api/envios/{envio.Id}/devolucion",
            new IniciarDevolucionRequest("Ya no lo vamos a enviar"));

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal("EnDevolucion", await EstadoAsync(envio.Id));
        Assert.Equal("PortalComercio", await ConsultarAsync<string>(
            """SELECT "Origen" FROM envios."EventosEnvio" WHERE "EnvioId" = @id AND "EstadoNuevo" = 'EnDevolucion'""", envio.Id));
        Assert.Equal(1L, await MensajesAsync(envio.Id));
    }

    // A2: el comercio no puede pedirla con el envío fuera del depósito (400 por regla de negocio).
    [Fact]
    public async Task El_comercio_no_la_pide_fuera_del_deposito()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: false);
        using var comercio = await ComercioAsync(DatosIniciales.OperadorDemoId);

        var respuesta = await comercio.PostAsJsonAsync($"/api/envios/{envio.Id}/devolucion",
            new IniciarDevolucionRequest("Ya no lo quiero"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("Admitido", await EstadoAsync(envio.Id));
        Assert.Equal(0L, await MensajesAsync(envio.Id));
    }

    // Perfil: el operario de depósito no ve el botón ni entra escribiendo la dirección (403).
    [Fact]
    public async Task El_operario_de_deposito_no_ve_el_boton_ni_entra()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");
        using var deposito = Cliente();
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        var enlace = $"/backoffice/envios/{envio.Numero}/devolucion";

        Assert.Contains(enlace, await despacho.GetStringAsync($"/backoffice/envios/{envio.Numero}"));
        Assert.DoesNotContain(enlace, await deposito.GetStringAsync($"/backoffice/envios/{envio.Numero}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await deposito.GetAsync(enlace)).StatusCode);
    }

    // Aislamiento (ADR-0002, sección 2.9): ni el despachador ni el comercio en la sesión de otro operador encuentran el envío.
    [Fact]
    public async Task Desde_otro_operador_responde_404_y_el_envio_no_cambia()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var despachoEste = Cliente();
        await Sesion.IngresarAsync(despachoEste, "despacho@envios-del-este.uy");
        using var comercioEnEste = await ComercioAsync(DatosIniciales.OperadorEsteId);

        Assert.Equal(HttpStatusCode.NotFound, (await despachoEste.GetAsync($"/backoffice/envios/{envio.Numero}/devolucion")).StatusCode);
        var porApi = await comercioEnEste.PostAsJsonAsync($"/api/envios/{envio.Id}/devolucion", new IniciarDevolucionRequest("Otro operador"));
        Assert.Equal(HttpStatusCode.NotFound, porApi.StatusCode);
        Assert.Equal("EnDeposito", await EstadoAsync(envio.Id));
    }

    private static Task<HttpResponseMessage> IniciarDesdeBackofficeAsync(HttpClient client, string numero, string motivo)
    {
        return Sesion.EnviarFormularioAsync(client, $"/backoffice/envios/{numero}/devolucion", new() { ["Motivo"] = motivo });
    }

    // El usuario del comercio con el operador elegido en la sesión (CU-03).
    private async Task<HttpClient> ComercioAsync(Guid operadorId)
    {
        var client = Cliente();
        await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(client, operadorId);
        return client;
    }

    // Un envío del comercio demo, admitido (y, si se pide, recibido en el depósito), guardado con el operador indicado.
    private async Task<Envio> EnvioAsync(Guid operadorId, bool enDeposito)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        var ahora = DateTimeOffset.UtcNow;
        var envio = Envio.Crear(operadorId, DatosIniciales.ComercioDemoId, $"ENV-T-{Guid.NewGuid():N}".ToUpperInvariant(),
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, ahora.AddHours(-2));
        if (enDeposito)
        {
            envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora.AddHours(-1));
        }

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(operadorId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    private Task<string> EstadoAsync(Guid envioId) =>
        ConsultarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id" = @id""", envioId);

    private Task<long> MensajesAsync(Guid envioId) => ConsultarAsync<long>(
        """SELECT count(*) FROM envios.outbox_messages WHERE "Tipo" = 'DevolucionIniciada.v1' AND "Contenido"::jsonb ->> 'EnvioId' = @id::text""",
        envioId);

    private async Task<T> ConsultarAsync<T>(string sql, Guid envioId)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", envioId);
        return (T)(await comando.ExecuteScalarAsync())!;
    }
}
