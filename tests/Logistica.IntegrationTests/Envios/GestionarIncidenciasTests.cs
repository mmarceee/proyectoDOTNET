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

// CU-22 de punta a punta desde el Backoffice, con sesión real y el respaldo provisorio apagado.
public class GestionarIncidenciasTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Listado = "/backoffice/incidencias";

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    // Flujo principal: Abierta → En proceso → Resuelta, sin cambiar el estado del envío. A2: sin resolución no se resuelve.
    [Fact]
    public async Task El_despachador_registra_atiende_y_resuelve_una_incidencia()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var registrada = await RegistrarAsync(despacho, envio.Numero, "DañoEnBulto", "La caja llegó aplastada");

        Assert.Equal(HttpStatusCode.Redirect, registrada.StatusCode);
        Assert.Equal($"/backoffice/envios/{envio.Numero}", registrada.Headers.Location?.OriginalString);
        var id = await ConsultarAsync<Guid>("""SELECT "Id" FROM envios."Incidencias" WHERE "EnvioId" = @id""", envio.Id);
        Assert.Equal("Abierta", await EstadoIncidenciaAsync(id));
        Assert.Equal("Admitido", await ConsultarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id" = @id""", envio.Id));
        Assert.Contains("La caja llegó aplastada", WebUtility.HtmlDecode(await despacho.GetStringAsync(Listado)));

        Assert.Equal(HttpStatusCode.Redirect, (await AccionAsync(despacho, "EnProceso", id)).StatusCode);
        Assert.Equal("EnProceso", await EstadoIncidenciaAsync(id));

        var sinResolucion = await AccionAsync(despacho, "Resolver", id, resolucion: "   ");
        Assert.Equal(HttpStatusCode.OK, sinResolucion.StatusCode); // vuelve a mostrar el listado con el aviso
        Assert.Contains("Indicá cómo se resolvió la incidencia.", WebUtility.HtmlDecode(await sinResolucion.Content.ReadAsStringAsync()));
        Assert.Equal("EnProceso", await EstadoIncidenciaAsync(id));

        Assert.Equal(HttpStatusCode.Redirect, (await AccionAsync(despacho, "Resolver", id, "Se repuso la mercadería")).StatusCode);
        Assert.Equal("Resuelta", await EstadoIncidenciaAsync(id));
        Assert.Equal("Se repuso la mercadería", await ConsultarAsync<string>(
            """SELECT "Resolucion" FROM envios."Incidencias" WHERE "Id" = @id""", id));
        Assert.True(await ConsultarAsync<bool>("""SELECT "ResueltaEn" IS NOT NULL FROM envios."Incidencias" WHERE "Id" = @id""", id));
    }

    // A1: el listado filtra por tipo y por estado.
    [Fact]
    public async Task El_listado_filtra_por_tipo_y_estado()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");
        await RegistrarAsync(despacho, envio.Numero, "Reclamo", "Reclamo por la demora");
        await RegistrarAsync(despacho, envio.Numero, "Otro", "Dirección mal escrita");

        var reclamos = WebUtility.HtmlDecode(await despacho.GetStringAsync($"{Listado}?Tipo=Reclamo"));
        var resueltas = WebUtility.HtmlDecode(await despacho.GetStringAsync($"{Listado}?Estado=Resuelta"));

        Assert.Contains("Reclamo por la demora", reclamos);
        Assert.DoesNotContain("Dirección mal escrita", reclamos);
        Assert.DoesNotContain("Reclamo por la demora", resueltas);
    }

    // Aislamiento (ADR-0002, secciones 2.1 y 2.9): otro operador no ve la incidencia ni la puede resolver, y el
    // operario de depósito no entra al listado.
    [Fact]
    public async Task Otro_operador_no_ve_ni_resuelve_la_incidencia()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");
        await RegistrarAsync(despacho, envio.Numero, "Extravio", "No aparece el bulto");
        var id = await ConsultarAsync<Guid>("""SELECT "Id" FROM envios."Incidencias" WHERE "EnvioId" = @id""", envio.Id);
        using var este = Cliente();
        await Sesion.IngresarAsync(este, "despacho@envios-del-este.uy");
        using var deposito = Cliente();
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");

        Assert.DoesNotContain("No aparece el bulto", WebUtility.HtmlDecode(await este.GetStringAsync(Listado)));
        Assert.Equal(HttpStatusCode.NotFound, (await AccionAsync(este, "Resolver", id, "Intento ajeno")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await este.GetAsync($"/backoffice/envios/{envio.Numero}/incidencias/nueva")).StatusCode);
        Assert.Equal("Abierta", await EstadoIncidenciaAsync(id));
        Assert.Equal(HttpStatusCode.Forbidden, (await deposito.GetAsync(Listado)).StatusCode);
    }

    private static Task<HttpResponseMessage> RegistrarAsync(HttpClient client, string numero, string tipo, string descripcion)
    {
        return Sesion.EnviarFormularioAsync(client, $"/backoffice/envios/{numero}/incidencias/nueva",
            new() { ["Tipo"] = tipo, ["Descripcion"] = descripcion });
    }

    // Los botones de cada fila del listado. El token antifalsificación es el de la sesión: se toma del listado, que lo
    // tiene siempre en el formulario de cerrar sesión, aunque no muestre ninguna fila.
    private static async Task<HttpResponseMessage> AccionAsync(HttpClient client, string accion, Guid id, string? resolucion = null)
    {
        var campos = new Dictionary<string, string> { ["__RequestVerificationToken"] = await Sesion.TokenAsync(client, Listado) };
        if (resolucion is not null)
        {
            campos["resolucion"] = resolucion;
        }

        return await client.PostAsync($"{Listado}?handler={accion}&id={id}", new FormUrlEncodedContent(campos));
    }

    // Un envío admitido, guardado con el operador indicado como inquilino.
    private async Task<Envio> EnvioAsync(Guid operadorId)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        var envio = Envio.Crear(operadorId, DatosIniciales.ComercioDemoId, $"ENV-T-{Guid.NewGuid():N}".ToUpperInvariant(),
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow.AddHours(-1));

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(operadorId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    private Task<string> EstadoIncidenciaAsync(Guid id) =>
        ConsultarAsync<string>("""SELECT "Estado" FROM envios."Incidencias" WHERE "Id" = @id""", id);

    private async Task<T> ConsultarAsync<T>(string sql, Guid id)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", id);
        return (T)(await comando.ExecuteScalarAsync())!;
    }
}
