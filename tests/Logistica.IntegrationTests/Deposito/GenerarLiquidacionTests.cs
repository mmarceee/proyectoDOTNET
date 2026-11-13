using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Npgsql;

namespace Logistica.IntegrationTests.Deposito;

// CU-32 de punta a punta: el administrador genera la liquidación del comercio demo desde el Backoffice, la emite y
// registra el pago. Cada prueba usa un mes propio para no mezclar sus envíos con los de las demás.
public class GenerarLiquidacionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Pagina = "/backoffice/liquidaciones";
    private static readonly Guid ComercioDemo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Cobra_los_entregados_y_devueltos_del_periodo_una_sola_vez_y_se_emite_y_se_paga()
    {
        using var comercio = factory.CreateClient();
        var entregado = await CrearEnvioAsync(comercio);
        var devuelto = await CrearEnvioAsync(comercio);
        var cancelado = await CrearEnvioAsync(comercio);
        var deOtroMes = await CrearEnvioAsync(comercio);
        // 31/3 a las 23:30 en Montevideo es el 1/4 en UTC: igual es de marzo, porque el período son días del operador.
        var ultimaHora = await CrearEnvioAsync(comercio);
        await CerrarAsync(entregado, "Entregado", new DateTimeOffset(2025, 3, 10, 15, 0, 0, TimeSpan.Zero));
        await CerrarAsync(devuelto, "Devuelto", new DateTimeOffset(2025, 3, 20, 15, 0, 0, TimeSpan.Zero));
        await CerrarAsync(cancelado, "Cancelado", new DateTimeOffset(2025, 3, 12, 15, 0, 0, TimeSpan.Zero));
        await CerrarAsync(deOtroMes, "Entregado", new DateTimeOffset(2025, 4, 2, 15, 0, 0, TimeSpan.Zero));
        await CerrarAsync(ultimaHora, "Entregado", new DateTimeOffset(2025, 3, 31, 23, 30, 0, TimeSpan.FromHours(-3)));

        using var admin = await AdminAsync();
        var respuesta = await GenerarAsync(admin, "2025-03-01", "2025-03-31");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var detalle = respuesta.Headers.Location!.ToString();
        Assert.Matches($"^{Pagina}/[0-9a-f-]{{36}}$", detalle);
        var id = Guid.Parse(detalle[(Pagina.Length + 1)..]);
        Assert.Equal("Borrador", await ConsultarAsync<string>("""SELECT "Estado" FROM deposito."Liquidaciones" WHERE "Id" = @id""", id));
        Assert.Equal(3L, await ConsultarAsync<long>("""SELECT count(*) FROM deposito."LineasLiquidacion" WHERE "LiquidacionId" = @id""", id));
        // Un bulto de 2 kg cuesta 150 con la versión 1 del tarifario demo.
        Assert.Equal(450m, await ConsultarAsync<decimal>("""SELECT "TotalNeto" FROM deposito."Liquidaciones" WHERE "Id" = @id""", id));
        foreach (var incluido in new[] { entregado, devuelto, ultimaHora })
            Assert.Equal(1L, await LineasDelEnvioAsync(incluido));
        foreach (var excluido in new[] { cancelado, deOtroMes })
            Assert.Equal(0L, await LineasDelEnvioAsync(excluido));

        var html = WebUtility.HtmlDecode(await admin.GetStringAsync(detalle));
        Assert.Contains("Tienda Demo SA", html);
        Assert.Contains("Envío devuelto", html);

        // Requerimiento 3 y A2: generarla otra vez no duplica líneas; sin envíos nuevos no se genera (A1).
        var otraVez = await GenerarAsync(admin, "2025-03-01", "2025-03-31");
        Assert.Equal(Pagina, otraVez.Headers.Location?.ToString());
        Assert.Contains("no tiene envíos ni recargos sin liquidar", WebUtility.HtmlDecode(await admin.GetStringAsync(Pagina)));
        Assert.Equal(1L, await LineasDelEnvioAsync(entregado));

        // Paso 6 y A3.
        Assert.Equal(HttpStatusCode.Redirect, (await AccionAsync(admin, id, "Emitir")).StatusCode);
        Assert.Equal("Emitida", await ConsultarAsync<string>("""SELECT "Estado" FROM deposito."Liquidaciones" WHERE "Id" = @id""", id));
        await AccionAsync(admin, id, "Pagada");
        Assert.Equal("Pagada", await ConsultarAsync<string>("""SELECT "Estado" FROM deposito."Liquidaciones" WHERE "Id" = @id""", id));
        await AccionAsync(admin, id, "Emitir");
        Assert.Contains("Sólo se puede emitir", WebUtility.HtmlDecode(await admin.GetStringAsync(detalle)));
        Assert.Equal("Pagada", await ConsultarAsync<string>("""SELECT "Estado" FROM deposito."Liquidaciones" WHERE "Id" = @id""", id));
    }

    [Fact]
    public async Task La_liquidacion_de_un_operador_no_la_ve_otro_ni_un_usuario_sin_perfil_de_administrador()
    {
        using var comercio = factory.CreateClient();
        var envio = await CrearEnvioAsync(comercio);
        await CerrarAsync(envio, "Entregado", new DateTimeOffset(2025, 5, 10, 15, 0, 0, TimeSpan.Zero));
        using var admin = await AdminAsync();
        var detalle = (await GenerarAsync(admin, "2025-05-01", "2025-05-31")).Headers.Location!.ToString();

        using var otroOperador = await AdminAsync("admin@envios-del-este.uy");
        Assert.Equal(HttpStatusCode.NotFound, (await otroOperador.GetAsync(detalle)).StatusCode);
        Assert.DoesNotContain(detalle, await otroOperador.GetStringAsync(Pagina));

        using var deposito = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        Assert.NotEqual(HttpStatusCode.OK, (await deposito.GetAsync(detalle)).StatusCode);
    }

    [Fact]
    public async Task Un_borrador_se_elimina_y_sus_envios_entran_en_otra_liquidacion_pero_una_emitida_no_se_elimina()
    {
        using var comercio = factory.CreateClient();
        var envio = await CrearEnvioAsync(comercio);
        await CerrarAsync(envio, "Entregado", new DateTimeOffset(2025, 7, 10, 15, 0, 0, TimeSpan.Zero));
        using var admin = await AdminAsync();
        var borrador = IdDe((await GenerarAsync(admin, "2025-07-01", "2025-07-31")).Headers.Location!);

        // Otro operador no la encuentra.
        using var otroOperador = await AdminAsync("admin@envios-del-este.uy");
        Assert.Equal(HttpStatusCode.NotFound, (await AccionAsync(otroOperador, borrador, "Eliminar")).StatusCode);

        var eliminar = await AccionAsync(admin, borrador, "Eliminar");
        Assert.Equal(Pagina, eliminar.Headers.Location?.ToString());
        Assert.Contains("Se eliminó el borrador", WebUtility.HtmlDecode(await admin.GetStringAsync(Pagina)));
        Assert.Equal(0L, await ConsultarAsync<long>("""SELECT count(*) FROM deposito."Liquidaciones" WHERE "Id" = @id""", borrador));
        Assert.Equal(0L, await LineasDelEnvioAsync(envio));

        // El envío quedó libre: la nueva liquidación lo incluye.
        var emitida = IdDe((await GenerarAsync(admin, "2025-07-01", "2025-07-31")).Headers.Location!);
        Assert.NotEqual(borrador, emitida);
        Assert.Equal(1L, await LineasDelEnvioAsync(envio));

        await AccionAsync(admin, emitida, "Emitir");
        var rechazo = await AccionAsync(admin, emitida, "Eliminar");
        Assert.Equal($"{Pagina}/{emitida}", rechazo.Headers.Location?.ToString());
        Assert.Contains("Sólo se puede eliminar", WebUtility.HtmlDecode(await admin.GetStringAsync($"{Pagina}/{emitida}")));
        Assert.Equal(1L, await LineasDelEnvioAsync(envio));
    }

    // Cierre técnico D8: el cierre mixto cobra la base una vez, entera; un envío sólo con bultos cancelados o extraviados
    // no se cobra; los recargos por nueva visita van en ajustes, por la fecha de la visita, aunque el envío no haya
    // terminado; los importes quedan en pesos enteros, y generar otra vez no cobra dos veces ningún concepto.
    [Fact]
    public async Task Cierre_mixto_recargos_y_pesos_enteros_segun_D8()
    {
        using var comercio = factory.CreateClient();
        var mixto = await CrearEnvioAsync(comercio, bultos: 3);
        var perdido = await CrearEnvioAsync(comercio, bultos: 2);
        var conRecargo = await CrearEnvioAsync(comercio);
        var mayo = new DateTimeOffset(2025, 5, 10, 15, 0, 0, TimeSpan.Zero);
        await CerrarBultosAsync(mixto, ["Entregado", "Entregado", "Devuelto"], mayo);
        await CerrarBultosAsync(perdido, ["Cancelado", "Extraviado"], mayo);
        // El envío del recargo sigue abierto: igual se cobra su visita adicional, con 0,50 que sube al peso siguiente.
        var recargo = await RecargoAsync(conRecargo, 120.5m, mayo.AddDays(2));

        using var admin = await AdminAsync();
        var respuesta = await GenerarAsync(admin, "2025-05-01", "2025-05-31");
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var id = IdDe(respuesta.Headers.Location!);

        // Tres bultos de 2 kg a 150 cada uno: la base del envío es 450, una sola vez.
        Assert.Equal(450m, await ConsultarAsync<decimal>("""SELECT "TotalBruto" FROM deposito."Liquidaciones" WHERE "Id" = @id""", id));
        Assert.Equal(121m, await ConsultarAsync<decimal>("""SELECT "TotalAjustes" FROM deposito."Liquidaciones" WHERE "Id" = @id""", id));
        Assert.Equal(571m, await ConsultarAsync<decimal>("""SELECT "TotalNeto" FROM deposito."Liquidaciones" WHERE "Id" = @id""", id));
        Assert.Equal(1L, await LineasDelEnvioAsync(mixto));
        Assert.Equal(0L, await LineasDelEnvioAsync(perdido));
        Assert.Equal(1L, await ConsultarAsync<long>(
            """SELECT count(*) FROM deposito."LineasLiquidacion" WHERE "Origen" = 'RecargoVisita' AND "IdDeOrigen" = @id""", recargo));
        var html = WebUtility.HtmlDecode(await admin.GetStringAsync(respuesta.Headers.Location!.ToString()));
        Assert.Contains("Envío entregado parcialmente", html);
        Assert.Contains("Recargo por nueva visita", html);

        // Cuando ese envío termine, su base entra en otro período sin volver a cobrar el recargo.
        await CerrarBultosAsync(conRecargo, ["Entregado"], new DateTimeOffset(2025, 6, 3, 15, 0, 0, TimeSpan.Zero));
        var junio = await GenerarAsync(admin, "2025-05-01", "2025-06-30");
        var idJunio = IdDe(junio.Headers.Location!);
        Assert.Equal(150m, await ConsultarAsync<decimal>("""SELECT "TotalNeto" FROM deposito."Liquidaciones" WHERE "Id" = @id""", idJunio));
        Assert.Equal(1L, await ConsultarAsync<long>(
            """SELECT count(*) FROM deposito."LineasLiquidacion" WHERE "IdDeOrigen" = @id""", recargo));
    }

    private static Guid IdDe(Uri detalle) => Guid.Parse(detalle.ToString()[(Pagina.Length + 1)..]);

    private async Task<HttpClient> AdminAsync(string email = "admin@operador-demo.uy")
    {
        var client = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, email);
        return client;
    }

    private static Task<HttpResponseMessage> GenerarAsync(HttpClient admin, string desde, string hasta) =>
        Sesion.EnviarFormularioAsync(admin, Pagina, new()
        {
            ["ComercioId"] = ComercioDemo.ToString(), ["Desde"] = desde, ["Hasta"] = hasta,
        });

    private static async Task<HttpResponseMessage> AccionAsync(HttpClient admin, Guid id, string accion)
    {
        // El token es de la sesión, no de la página: se toma del listado, que siempre tiene el formulario de generar.
        var token = await Sesion.TokenAsync(admin, Pagina);
        return await admin.PostAsync($"{Pagina}/{id}?handler={accion}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    private static async Task<Guid> CrearEnvioAsync(HttpClient comercio, int bultos = 1)
    {
        var respuesta = await comercio.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", null, null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
            Enumerable.Repeat(new BultoRequest(2, 30, 20, 10), bultos).ToList()));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!.Id;
    }

    // Atajo de la prueba: lleva el envío y sus bultos a un estado final sin recorrer la ruta, con el instante de cierre
    // (TerminadoEn) que lee la liquidación (D8). El evento de esa transición reemplaza al de la admisión.
    private async Task CerrarAsync(Guid envioId, string estado, DateTimeOffset en)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            UPDATE envios."Envios" SET "Estado" = @estado, "Terminado" = true, "TerminadoEn" = @en WHERE "Id" = @id;
            UPDATE envios."Bultos" SET "Estado" = @estado WHERE "EnvioId" = @id;
            UPDATE envios."EventosEnvio" SET "EstadoNuevo" = @estado, "OcurridoEn" = @en WHERE "EnvioId" = @id;
            """, conexion);
        comando.Parameters.AddWithValue("id", envioId);
        comando.Parameters.AddWithValue("estado", estado);
        comando.Parameters.AddWithValue("en", en.ToUniversalTime());
        await comando.ExecuteNonQueryAsync();
    }

    // Como CerrarAsync, con un estado final por bulto (en el orden de sus códigos): un cierre mixto.
    private async Task CerrarBultosAsync(Guid envioId, string[] estados, DateTimeOffset en)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            UPDATE envios."Bultos" b SET "Estado" = (@estados)[o.n]
            FROM (SELECT "Id", row_number() OVER (ORDER BY "Codigo") AS n FROM envios."Bultos" WHERE "EnvioId" = @id) o
            WHERE b."Id" = o."Id";
            UPDATE envios."Envios" SET "Terminado" = true, "TerminadoEn" = @en WHERE "Id" = @id;
            """, conexion);
        comando.Parameters.AddWithValue("id", envioId);
        comando.Parameters.AddWithValue("estados", estados);
        comando.Parameters.AddWithValue("en", en.ToUniversalTime());
        await comando.ExecuteNonQueryAsync();
    }

    // Un recargo por nueva visita ya registrado (RF 33), con el monto de ese momento.
    private async Task<Guid> RecargoAsync(Guid envioId, decimal monto, DateTimeOffset en)
    {
        var recargoId = Guid.NewGuid();
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            INSERT INTO envios."RecargosNuevaVisita" ("Id", "EnvioId", "OperadorId", "ComercioId", "Monto", "OcurridoEn")
            SELECT @recargo, "Id", "OperadorId", "ComercioId", @monto, @en FROM envios."Envios" WHERE "Id" = @id;
            """, conexion);
        comando.Parameters.AddWithValue("recargo", recargoId);
        comando.Parameters.AddWithValue("id", envioId);
        comando.Parameters.AddWithValue("monto", monto);
        comando.Parameters.AddWithValue("en", en.ToUniversalTime());
        await comando.ExecuteNonQueryAsync();
        return recargoId;
    }

    private Task<long> LineasDelEnvioAsync(Guid envioId) =>
        ConsultarAsync<long>("""SELECT count(*) FROM deposito."LineasLiquidacion" WHERE "EnvioId" = @id""", envioId);

    private async Task<T> ConsultarAsync<T>(string sql, Guid id)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", id);
        return (T)(await comando.ExecuteScalarAsync())!;
    }
}
