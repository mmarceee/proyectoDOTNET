using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Logistica.IntegrationTests.Envios;

// El comercio elige en el alta (CU-10) la fecha y la franja de entrega, entre las de la zona de su código postal; Armar
// ruta (CU-40) ofrece ese envío sólo ese día y lo encuentra al filtrar por esa franja.
public class FranjaEnAltaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    // Zona Montevideo del operador Demo (código postal 11300), con franjas de 9 a 13 y de 14 a 18 todos los días.
    private static readonly Guid FranjaManana = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly Guid FranjaTarde = Guid.Parse("72222222-2222-2222-2222-222222222222");
    // Del operador Este: no es de la zona de la dirección.
    private static readonly Guid FranjaEste = Guid.Parse("73333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Dia = new(2035, 4, 20);

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")).CreateClient();

    [Fact]
    public async Task El_comercio_ve_las_franjas_de_la_zona_del_codigo_postal_para_ese_dia()
    {
        using var client = Cliente();

        var franjas = (await client.GetFromJsonAsync<List<FranjaDisponibleResponse>>(
            $"/api/envios/franjas?codigoPostal=11300&fecha={Dia:yyyy-MM-dd}"))!;
        Assert.Equal([FranjaManana, FranjaTarde], franjas.Select(f => f.Id));
        Assert.Equal(new TimeOnly(9, 0), franjas[0].Desde);

        // Un código postal sin cobertura, o hoy (el envío todavía tiene que llegar al depósito): ninguna.
        Assert.Empty((await client.GetFromJsonAsync<List<FranjaDisponibleResponse>>(
            $"/api/envios/franjas?codigoPostal=99999&fecha={Dia:yyyy-MM-dd}"))!);
        Assert.Empty((await client.GetFromJsonAsync<List<FranjaDisponibleResponse>>(
            $"/api/envios/franjas?codigoPostal=11300&fecha={DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)):yyyy-MM-dd}"))!);
    }

    [Fact]
    public async Task El_envio_guarda_fecha_y_franja_y_Armar_ruta_lo_ofrece_solo_ese_dia_y_en_esa_franja()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client, Dia, FranjaManana);

        Assert.Equal("2035-04-20", await ConsultarAsync<string>("""SELECT "FechaEntregaProgramada"::text FROM envios."Envios" WHERE "Id" = @id""", envio.Id));
        Assert.Equal(FranjaManana, await ConsultarAsync<Guid>("""SELECT "FranjaHorariaId" FROM envios."Envios" WHERE "Id" = @id""", envio.Id));

        Assert.True(await DisponibleAsync(client, envio, Dia, franja: null));
        Assert.True(await DisponibleAsync(client, envio, Dia, FranjaManana));
        Assert.False(await DisponibleAsync(client, envio, Dia, FranjaTarde));
        Assert.False(await DisponibleAsync(client, envio, Dia.AddDays(1), franja: null));

        // El filtro de Armar ruta nombra cada franja con su zona: dos zonas pueden tener el mismo horario.
        var pagina = WebUtility.HtmlDecode(await client.GetStringAsync("/backoffice/rutas/nueva"));
        Assert.Contains("· 09:00–13:00", pagina);
    }

    [Fact]
    public async Task Sin_fecha_y_franja_el_envio_queda_disponible_cualquier_dia()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client, fecha: null, franja: null);

        Assert.True(await DisponibleAsync(client, envio, Dia, franja: null));
        Assert.True(await DisponibleAsync(client, envio, Dia.AddDays(3), franja: null));
        Assert.False(await DisponibleAsync(client, envio, Dia, FranjaManana));
    }

    [Theory]
    [InlineData(true, false, "juntas")]
    [InlineData(false, true, "juntas")]
    public async Task La_fecha_y_la_franja_van_juntas(bool conFecha, bool conFranja, string motivo)
    {
        using var client = Cliente();

        var respuesta = await client.PostAsJsonAsync("/api/envios",
            Envio(conFecha ? Dia : null, conFranja ? FranjaManana : null));

        await ExigirRechazoAsync(respuesta, motivo);
    }

    [Fact]
    public async Task Una_franja_de_otra_zona_o_una_fecha_que_no_es_futura_se_rechazan()
    {
        using var client = Cliente();

        await ExigirRechazoAsync(await client.PostAsJsonAsync("/api/envios", Envio(Dia, FranjaEste)), "no está disponible");
        await ExigirRechazoAsync(await client.PostAsJsonAsync("/api/envios", Envio(new DateOnly(2020, 1, 1), FranjaManana)),
            "posterior a hoy");
    }

    private static CrearEnvioRequest Envio(DateOnly? fecha, Guid? franja) => new(
        new DestinatarioRequest("Ana", "099123456", null, null),
        new DireccionRequest("Av. Italia", Guid.NewGuid().ToString("N"), "Montevideo", "Montevideo", "11300", null),
        [new BultoRequest(2, 30, 20, 10)], "Estandar", fecha, franja);

    private static async Task<CrearEnvioResponse> RecibidoAsync(HttpClient client, DateOnly? fecha, Guid? franja)
    {
        var crear = await client.PostAsJsonAsync("/api/envios", Envio(fecha, franja));
        Assert.True(crear.IsSuccessStatusCode, await crear.Content.ReadAsStringAsync());
        var envio = (await crear.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        var recepcion = await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest($"{envio.Numero}-1", null));
        Assert.True(recepcion.IsSuccessStatusCode, await recepcion.Content.ReadAsStringAsync());
        return envio;
    }

    private static async Task<bool> DisponibleAsync(HttpClient client, CrearEnvioResponse envio, DateOnly fecha, Guid? franja)
    {
        var url = $"/api/planificacion/envios-disponibles?fecha={fecha:yyyy-MM-dd}&texto={envio.Numero}"
            + (franja is null ? "" : $"&franjaId={franja}");
        var disponibles = (await client.GetFromJsonAsync<EnviosDisponiblesResponse>(url))!;
        return disponibles.Items.Any(e => e.Id == envio.Id);
    }

    private static async Task ExigirRechazoAsync(HttpResponseMessage respuesta, string motivo)
    {
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains(motivo, problema?.Detail);
    }

    private async Task<T> ConsultarAsync<T>(string sql, Guid id)
    {
        await using var db = new NpgsqlConnection(factory.ConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("id", id);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
