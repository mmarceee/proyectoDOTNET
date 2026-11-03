using System.Net;
using System.Net.Http.Json;
using System.Text;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Npgsql;

namespace Logistica.IntegrationTests.Deposito;

// CU-33 de punta a punta: el operador genera y emite liquidaciones en el Backoffice (CU-32) y el comercio las consulta
// desde el Portal. Usa meses propios (septiembre y octubre de 2025) para no mezclarse con GenerarLiquidacionTests.
public class ConsultarCuentaCorrienteTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Cuenta = "/api/cuenta-corriente";
    private const string Liquidaciones = "/backoffice/liquidaciones";

    [Fact]
    public async Task El_comercio_ve_sus_liquidaciones_emitidas_con_el_saldo_y_el_detalle_pero_no_los_borradores()
    {
        using var alta = factory.CreateClient();
        var septiembre = await CrearEnvioAsync(alta);
        var octubre = await CrearEnvioAsync(alta);
        // 30/9 a las 22:00 en Montevideo ya es el 1/10 en UTC: la línea lleva el día del operador.
        await CerrarAsync(septiembre.Id, "Entregado", new DateTimeOffset(2025, 9, 30, 22, 0, 0, TimeSpan.FromHours(-3)));
        await CerrarAsync(octubre.Id, "Devuelto", new DateTimeOffset(2025, 10, 15, 15, 0, 0, TimeSpan.Zero));

        using var admin = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");
        var deSeptiembre = await GenerarAsync(admin, "2025-09-01", "2025-09-30");
        var deOctubre = await GenerarAsync(admin, "2025-10-01", "2025-10-31");

        using var comercio = await ComercioAsync(DatosIniciales.OperadorDemoId);
        var antes = await CuentaAsync(comercio);

        // Requerimiento 1: los dos son borradores y el comercio no los ve.
        Assert.DoesNotContain(antes.Liquidaciones, l => l.Id == deSeptiembre || l.Id == deOctubre);
        Assert.Equal(HttpStatusCode.NotFound, (await comercio.GetAsync($"{Cuenta}/liquidaciones/{deSeptiembre}")).StatusCode);

        // Emitida: aparece y suma al saldo pendiente. Un bulto de 2 kg cuesta 150 con el tarifario demo.
        await AccionAsync(admin, deSeptiembre, "Emitir");
        var emitida = await CuentaAsync(comercio);
        Assert.Equal(antes.SaldoPendiente + 150m, emitida.SaldoPendiente);
        var resumen = Assert.Single(emitida.Liquidaciones, l => l.Id == deSeptiembre);
        Assert.Equal(("Emitida", new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30), 150m),
            (resumen.Estado, resumen.PeriodoDesde, resumen.PeriodoHasta, resumen.TotalNeto));
        Assert.DoesNotContain(emitida.Liquidaciones, l => l.Id == deOctubre);

        var detalle = await comercio.GetFromJsonAsync<LiquidacionDetalleResponse>($"{Cuenta}/liquidaciones/{deSeptiembre}");
        var linea = Assert.Single(detalle!.Lineas);
        Assert.Equal((septiembre.Numero, new DateOnly(2025, 9, 30), "Envío entregado", 150m),
            (linea.NumeroEnvio, linea.Fecha, linea.Concepto, linea.Monto));

        // Paso 5: la descarga.
        var archivo = await comercio.GetAsync($"{Cuenta}/liquidaciones/{deSeptiembre}/archivo");
        Assert.Equal(HttpStatusCode.OK, archivo.StatusCode);
        Assert.Equal("text/csv", archivo.Content.Headers.ContentType?.MediaType);
        Assert.Contains("liquidacion-2025-09-01-al-2025-09-30.csv", archivo.Content.Headers.ContentDisposition?.ToString());
        var csv = Encoding.UTF8.GetString(await archivo.Content.ReadAsByteArrayAsync());
        Assert.Contains($"{septiembre.Numero};30/09/2025;Envío entregado;150,00", csv);

        // Pagada: sigue en la lista, pero ya no se debe.
        await AccionAsync(admin, deSeptiembre, "Pagada");
        var pagada = await CuentaAsync(comercio);
        Assert.Equal(antes.SaldoPendiente, pagada.SaldoPendiente);
        Assert.Equal("Pagada", Assert.Single(pagada.Liquidaciones, l => l.Id == deSeptiembre).Estado);
    }

    [Fact]
    public async Task Con_otro_operador_el_comercio_no_ve_esas_liquidaciones_y_el_personal_no_entra()
    {
        using var alta = factory.CreateClient();
        var envio = await CrearEnvioAsync(alta);
        await CerrarAsync(envio.Id, "Entregado", new DateTimeOffset(2025, 11, 10, 15, 0, 0, TimeSpan.Zero));
        using var admin = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");
        var id = await GenerarAsync(admin, "2025-11-01", "2025-11-30");
        await AccionAsync(admin, id, "Emitir");

        // El mismo comercio trabaja con Envíos del Este: con ese operador en la sesión no la ve (ADR-0002).
        using var conEste = await ComercioAsync(DatosIniciales.OperadorEsteId);
        Assert.DoesNotContain((await CuentaAsync(conEste)).Liquidaciones, l => l.Id == id);
        Assert.Equal(HttpStatusCode.NotFound, (await conEste.GetAsync($"{Cuenta}/liquidaciones/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await conEste.GetAsync($"{Cuenta}/liquidaciones/{id}/archivo")).StatusCode);

        // La cuenta corriente es del Portal: el administrador del operador no la consulta por esta API.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Cuenta)).StatusCode);
    }

    private async Task<HttpClient> ComercioAsync(Guid operadorId)
    {
        var client = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(client, operadorId);
        return client;
    }

    private static async Task<CuentaCorrienteResponse> CuentaAsync(HttpClient comercio) =>
        (await comercio.GetFromJsonAsync<CuentaCorrienteResponse>(Cuenta))!;

    private static async Task<Guid> GenerarAsync(HttpClient admin, string desde, string hasta)
    {
        var respuesta = await Sesion.EnviarFormularioAsync(admin, Liquidaciones, new()
        {
            ["ComercioId"] = DatosIniciales.ComercioDemoId.ToString(), ["Desde"] = desde, ["Hasta"] = hasta,
        });
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return Guid.Parse(respuesta.Headers.Location!.ToString()[(Liquidaciones.Length + 1)..]);
    }

    private static async Task AccionAsync(HttpClient admin, Guid id, string accion)
    {
        var token = await Sesion.TokenAsync(admin, Liquidaciones);
        var respuesta = await admin.PostAsync($"{Liquidaciones}/{id}?handler={accion}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
    }

    private static async Task<CrearEnvioResponse> CrearEnvioAsync(HttpClient comercio)
    {
        var respuesta = await comercio.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", null, null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
    }

    // El mismo atajo que GenerarLiquidacionTests: lleva el envío a su estado final sin recorrer la ruta.
    private async Task CerrarAsync(Guid envioId, string estado, DateTimeOffset en)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            UPDATE envios."Envios" SET "Estado" = @estado WHERE "Id" = @id;
            UPDATE envios."EventosEnvio" SET "EstadoNuevo" = @estado, "OcurridoEn" = @en WHERE "EnvioId" = @id;
            """, conexion);
        comando.Parameters.AddWithValue("id", envioId);
        comando.Parameters.AddWithValue("estado", estado);
        comando.Parameters.AddWithValue("en", en.ToUniversalTime());
        await comando.ExecuteNonQueryAsync();
    }
}
