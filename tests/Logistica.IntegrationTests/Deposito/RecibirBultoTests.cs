using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Logistica.IntegrationTests.Deposito;

// CU-30 de punta a punta: Depósito registra la recepción y le pide a Envíos la transición T2,
// las dos cosas en la misma transacción.
public class RecibirBultoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly BultoRequest UnBulto = new(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10);

    [Fact]
    public async Task Al_recibir_todos_los_bultos_el_envio_pasa_a_EnDeposito()
    {
        var client = factory.CreateClient();
        var envio = await CrearEnvioAsync(client, bultos: 2);

        var primero = await RecibirAsync(client, new RecibirBultoRequest($"{envio.Numero}-1"));

        Assert.False(primero.EnvioEnDeposito);
        Assert.Equal("Admitido", await EstadoAsync(envio.Id));

        var segundo = await RecibirAsync(client, new RecibirBultoRequest($"{envio.Numero}-2"));

        Assert.True(segundo.EnvioEnDeposito);
        Assert.All(segundo.Bultos, b => Assert.True(b.Recibido));
        Assert.Equal("EnDeposito", await EstadoAsync(envio.Id));
        Assert.Equal(2L, await ContarAsync("""SELECT count(*) FROM deposito."Recepciones" WHERE "EnvioId" = @id""", envio.Id));
        Assert.Equal(1L, await ContarAsync(
            """SELECT count(*) FROM envios."EventosEnvio" WHERE "EnvioId" = @id AND "EstadoNuevo" = 'EnDeposito'""", envio.Id));
    }

    [Fact]
    public async Task El_codigo_se_acepta_en_minusculas_y_con_espacios_como_lo_escribe_un_lector()
    {
        var client = factory.CreateClient();
        var envio = await CrearEnvioAsync(client, bultos: 1);

        var recepcion = await RecibirAsync(client, new RecibirBultoRequest($"  {envio.Numero.ToLowerInvariant()}-1 "));

        Assert.True(recepcion.EnvioEnDeposito);
    }

    [Fact]
    public async Task Una_diferencia_de_peso_se_registra_como_discrepancia_y_la_recepcion_sigue()
    {
        var client = factory.CreateClient();
        var envio = await CrearEnvioAsync(client, bultos: 1);

        var recepcion = await RecibirAsync(client, new RecibirBultoRequest($"{envio.Numero}-1", PesoKg: 5));

        Assert.Equal("ConDiscrepancia", recepcion.Resultado);
        Assert.Contains("peso", recepcion.Discrepancia);
        Assert.True(recepcion.EnvioEnDeposito);
    }

    [Fact]
    public async Task Un_bulto_ya_recibido_responde_400()
    {
        var client = factory.CreateClient();
        var envio = await CrearEnvioAsync(client, bultos: 2);
        await RecibirAsync(client, new RecibirBultoRequest($"{envio.Numero}-1"));

        var response = await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest($"{envio.Numero}-1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal($"El bulto {envio.Numero}-1 ya se escaneó.", problema?.Detail);
    }

    [Fact]
    public async Task Un_codigo_desconocido_responde_400_sin_mas_detalle()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest("ENV-999999-1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Bulto desconocido.", problema?.Detail);
    }

    private static async Task<CrearEnvioResponse> CrearEnvioAsync(HttpClient client, int bultos)
    {
        var request = new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", Email: null, Documento: null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", Referencia: null),
            Enumerable.Repeat(UnBulto, bultos).ToList());

        var response = await client.PostAsJsonAsync("/api/envios", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
    }

    private static async Task<RecibirBultoResponse> RecibirAsync(HttpClient client, RecibirBultoRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/deposito/recepciones", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RecibirBultoResponse>())!;
    }

    private async Task<string> EstadoAsync(Guid envioId)
    {
        return await EscalarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id" = @id""", envioId);
    }

    private async Task<long> ContarAsync(string sql, Guid envioId)
    {
        return await EscalarAsync<long>(sql, envioId);
    }

    // Lee directo de la base, sin pasar por la API.
    private async Task<T> EscalarAsync<T>(string sql, Guid envioId)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", envioId);

        return (T)(await comando.ExecuteScalarAsync())!;
    }
}
