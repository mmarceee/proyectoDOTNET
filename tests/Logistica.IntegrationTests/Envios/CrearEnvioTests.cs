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

        // Letra 6.6: el envío guarda la versión de reglas que regía al admitirlo.
        Assert.NotEqual(Guid.Empty, await ConsultarAsync<Guid>("""SELECT "VersionReglasId" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.Equal(300m, await ConsultarAsync<decimal>("""SELECT "MontoTarifa" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.Equal(150m, await ConsultarAsync<decimal>("""SELECT min("MontoTarifa") FROM envios."Bultos" WHERE "EnvioId" = @id""", creado.Id));
        Assert.Equal(Guid.Parse("61111111-1111-1111-1111-111111111111"),
            await ConsultarAsync<Guid>("""SELECT "ZonaId" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.NotEqual(Guid.Empty, await ConsultarAsync<Guid>("""SELECT "VersionTarifarioId" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.Equal(1, await ConsultarAsync<int>("""SELECT "VersionTarifarioNumero" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
    }

    [Fact]
    public async Task Cada_bulto_se_tarifa_segun_su_peso_y_el_total_es_la_suma()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/envios", UnEnvio(UnBulto, UnBulto with { PesoKg = 5 }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = (await response.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        Assert.Equal(450m, await ConsultarAsync<decimal>("""SELECT "MontoTarifa" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.Equal(300m, await ConsultarAsync<decimal>("""SELECT "MontoTarifa" FROM envios."Bultos" WHERE "EnvioId" = @id AND "PesoKg" = 5""", creado.Id));
    }

    [Theory]
    [InlineData("Estandar", 300)]
    [InlineData("Urgente", 500)]
    public async Task La_modalidad_elegida_determina_la_tarifa_y_se_conserva_en_el_detalle(string modalidad, decimal monto)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/envios", UnEnvio(UnBulto, UnBulto) with { Modalidad = modalidad });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = (await response.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        Assert.Equal(monto, await ConsultarAsync<decimal>("""SELECT "MontoTarifa" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.Equal(modalidad, await ConsultarAsync<string>("""SELECT "Modalidad" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/backoffice/envios/{creado.Numero}"));
        Assert.Contains(modalidad == "Urgente" ? "Urgente" : "Estándar", html);
        var listado = WebUtility.HtmlDecode(await client.GetStringAsync($"/backoffice/envios?Texto={creado.Numero}"));
        if (modalidad == "Urgente") Assert.Contains("badge text-bg-warning", listado);
        else Assert.DoesNotContain("badge text-bg-warning", listado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Inexistente")]
    [InlineData("1")]
    [InlineData(null)]
    public async Task Una_modalidad_invalida_no_crea_un_envio(string? modalidad)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/envios", UnEnvio(UnBulto) with { Modalidad = modalidad! });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Un_cliente_que_omite_la_modalidad_sigue_creando_envios_estandar()
    {
        var client = factory.CreateClient();
        var request = UnEnvio(UnBulto);
        var response = await client.PostAsJsonAsync("/api/envios", new
        {
            request.Destinatario, request.Direccion, request.Bultos,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = (await response.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        Assert.Equal("Estandar", await ConsultarAsync<string>("""SELECT "Modalidad" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
        Assert.Equal(150m, await ConsultarAsync<decimal>("""SELECT "MontoTarifa" FROM envios."Envios" WHERE "Id" = @id""", creado.Id));
    }

    [Theory]
    [InlineData(30, 20, 10, 30)] // El extremo superior del rango de peso queda fuera.
    [InlineData(100, 100, 100, 2)] // 1 m³: fuera del rango, aunque el peso sí tenga precio.
    [InlineData(100, 100, 10, 2)] // 0,1 m³: el extremo superior del rango de volumen queda fuera.
    public async Task Un_bulto_sin_tarifa_rechaza_todo_el_envio_sin_guardarlo(decimal largo, decimal ancho, decimal alto, decimal peso)
    {
        var client = factory.CreateClient();
        var antes = await ConsultarAsync<long>("""SELECT count(*) FROM envios."Envios" WHERE true""", Guid.Empty);
        var response = await client.PostAsJsonAsync("/api/envios", UnEnvio(UnBulto, new BultoRequest(peso, largo, ancho, alto)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("El bulto 2 no tiene una tarifa aplicable", problema?.Detail);
        Assert.Equal(antes, await ConsultarAsync<long>("""SELECT count(*) FROM envios."Envios" WHERE true""", Guid.Empty));
    }

    [Fact]
    public async Task Una_direccion_fuera_de_cobertura_no_crea_un_envio_con_tarifa_cero()
    {
        var client = factory.CreateClient();
        var request = UnEnvio(UnBulto);
        request = request with { Direccion = request.Direccion with { CodigoPostal = "99999" } };
        var response = await client.PostAsJsonAsync("/api/envios", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("no cubre el código postal", problema?.Detail);
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
