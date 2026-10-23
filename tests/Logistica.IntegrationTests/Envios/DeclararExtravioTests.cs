using System.Net;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Envios;

// CU-21 de punta a punta desde el Backoffice, con sesión real y el respaldo provisorio apagado.
public class DeclararExtravioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    // Flujo principal: el envío queda Extraviado, con su evento, la incidencia y el mensaje del Outbox (misma transacción).
    [Fact]
    public async Task El_despachador_declara_el_extravio_y_queda_todo_registrado()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var respuesta = await DeclararAsync(despacho, envio.Numero, "Robo de la camioneta", confirmo: true);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal($"/backoffice/envios/{envio.Numero}", respuesta.Headers.Location?.OriginalString);
        Assert.Equal("Extraviado", await EstadoAsync(envio.Id));
        Assert.Equal("Robo de la camioneta", await ConsultarAsync<string>(
            """SELECT "Detalle" FROM envios."EventosEnvio" WHERE "EnvioId" = @id AND "EstadoNuevo" = 'Extraviado'""", envio.Id));
        Assert.Equal("Robo de la camioneta", await ConsultarAsync<string>(
            """SELECT "Descripcion" FROM envios."Incidencias" WHERE "EnvioId" = @id AND "Tipo" = 'Extravio'""", envio.Id));
        Assert.Equal(1L, await MensajesAsync(envio.Id));
    }

    // A1 y requerimiento especial 1: sin motivo o sin la confirmación explícita, no cambia nada.
    [Theory]
    [InlineData("", true)]
    [InlineData("Robo", false)]
    public async Task Sin_motivo_o_sin_confirmar_no_cambia_nada(string motivo, bool confirmo)
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var respuesta = await DeclararAsync(despacho, envio.Numero, motivo, confirmo);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode); // vuelve a mostrar la página con el aviso
        Assert.Equal("EnDeposito", await EstadoAsync(envio.Id));
        Assert.Equal(0L, await MensajesAsync(envio.Id));
    }

    // A2: un envío que todavía no llegó al depósito no se puede declarar extraviado; la página muestra el motivo.
    [Fact]
    public async Task Desde_un_estado_no_permitido_se_informa_y_no_cambia()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: false);
        using var admin = Cliente();
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");

        var respuesta = await DeclararAsync(admin, envio.Numero, "No aparece", confirmo: true);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("no puede pasar de Admitido a Extraviado", WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
        Assert.Equal("Admitido", await EstadoAsync(envio.Id));
        Assert.Equal(0L, await MensajesAsync(envio.Id));
    }

    // Perfil (D2): el operario de depósito no ve el botón ni entra escribiendo la dirección (403).
    [Fact]
    public async Task El_operario_de_deposito_no_ve_el_boton_ni_entra()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");
        using var deposito = Cliente();
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        var enlace = $"/backoffice/envios/{envio.Numero}/extravio";

        Assert.Contains(enlace, await despacho.GetStringAsync($"/backoffice/envios/{envio.Numero}"));
        Assert.DoesNotContain(enlace, await deposito.GetStringAsync($"/backoffice/envios/{envio.Numero}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await deposito.GetAsync(enlace)).StatusCode);
    }

    // Aislamiento (ADR-0002, sección 2.9): el despachador de otro operador no encuentra el envío y no lo puede tocar.
    [Fact]
    public async Task El_despachador_de_otro_operador_recibe_404_y_el_envio_no_cambia()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId, enDeposito: true);
        using var este = Cliente();
        await Sesion.IngresarAsync(este, "despacho@envios-del-este.uy");

        Assert.Equal(HttpStatusCode.NotFound, (await este.GetAsync($"/backoffice/envios/{envio.Numero}/extravio")).StatusCode);
        Assert.Equal("EnDeposito", await EstadoAsync(envio.Id));
    }

    private static Task<HttpResponseMessage> DeclararAsync(HttpClient client, string numero, string motivo, bool confirmo)
    {
        return Sesion.EnviarFormularioAsync(client, $"/backoffice/envios/{numero}/extravio",
            new() { ["Motivo"] = motivo, ["Confirmo"] = confirmo ? "true" : "false" });
    }

    // Un envío admitido (y, si se pide, recibido en el depósito), guardado con el operador indicado como inquilino.
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
        """SELECT count(*) FROM envios.outbox_messages WHERE "Tipo" = 'EnvioExtraviado.v1' AND "Contenido"::jsonb ->> 'EnvioId' = @id::text""",
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
