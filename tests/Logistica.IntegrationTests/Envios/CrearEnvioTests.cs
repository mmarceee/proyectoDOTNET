using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Envios;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Logistica.IntegrationTests.Envios;

// CU-10 de punta a punta: HTTP, endpoint, handler, dominio, EF Core y PostgreSQL.
public class CrearEnvioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    // El operador del inquilino provisorio (appsettings.json de la API).
    private static readonly Guid OperadorProvisorio = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly BultoRequest UnBulto = new(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10);

    private static CrearEnvioRequest UnEnvio(params BultoRequest[] bultos) => new(
        new DestinatarioRequest("Ana Pérez", "099123456", Email: null, Documento: null),
        new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", Referencia: null),
        bultos);

    [Fact]
    public async Task Crear_un_envio_responde_201_y_lo_guarda_Admitido_con_sus_bultos_y_su_evento()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/envios", UnEnvio(UnBulto, UnBulto));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = await response.Content.ReadFromJsonAsync<CrearEnvioResponse>();
        Assert.NotNull(creado);
        Assert.StartsWith("ENV-", creado.Numero);
        Assert.Equal($"/api/envios/{creado.Id}", response.Headers.Location?.ToString());

        Assert.Equal("Admitido", await ConsultarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.Equal(OperadorProvisorio, await ConsultarAsync<Guid>("""SELECT "OperadorId" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.Equal(2L, await ConsultarAsync<long>("""SELECT count(*) FROM envios."Bultos" WHERE "EnvioId" = @id""", creado.Id));
        Assert.Equal(1L, await ConsultarAsync<long>("""SELECT count(*) FROM envios."EventosEnvio" WHERE "EnvioId" = @id""", creado.Id));
    }

    [Fact]
    public async Task Un_envio_sin_bultos_responde_400_con_el_motivo()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/envios", UnEnvio());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Un envío debe tener al menos un bulto.", problema?.Detail);
    }

    [Fact]
    public async Task Un_body_sin_direccion_responde_400()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/envios", new
        {
            destinatario = new { nombre = "Ana", telefono = "099123456", email = (string?)null, documento = (string?)null },
            bultos = new[] { UnBulto },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Lee directo de la base, sin pasar por la API, para comprobar lo que quedó guardado.
    private async Task<T> ConsultarAsync<T>(string sql, Guid envioId)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", envioId);

        return (T)(await comando.ExecuteScalarAsync())!;
    }
}
