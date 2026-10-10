using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

public sealed class RecepcionDesdeDetalleTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Conserva_filtros_al_abrir_registrar_y_volver_desde_recepcion()
    {
        var client = factory.CreateClient();
        var envio = await CrearAsync(client);
        const string parametros = "Pagina=3&TamanoPagina=5&Estado=Admitido&FechaDesde=2026-10-01&FechaHasta=2026-10-15&Texto=Ana%20%26%20P%C3%A9rez";
        var detalleUrl = $"/backoffice/envios/{envio.Numero}?{parametros}";
        var detalle = await client.GetStringAsync(detalleUrl);
        var enlace = Regex.Match(detalle, $"href=\"([^\"]+)\"[^>]+aria-label=\"Recepcionar bulto {envio.Numero}-1\"");
        Assert.True(enlace.Success);
        var recepcionUrl = WebUtility.HtmlDecode(enlace.Groups[1].Value);
        Assert.Contains("CodigoBulto=" + envio.Numero + "-1", recepcionUrl);
        var formulario = await client.GetStringAsync(recepcionUrl);
        Assert.Contains($"href=\"{detalleUrl}\"", WebUtility.HtmlDecode(formulario));
        Assert.DoesNotContain("value=\"2\"", Input(formulario, "PesoKg"));

        var campos = new Dictionary<string, string> { ["CodigoBulto"] = envio.Numero + "-1" };
        foreach (var nombre in new[] { "Pagina", "TamanoPagina", "Estado", "FechaDesde", "FechaHasta", "Texto", "__RequestVerificationToken" })
        {
            var valor = Regex.Match(Input(formulario, nombre), "value=\"([^\"]*)\"");
            Assert.True(valor.Success);
            campos[nombre] = WebUtility.HtmlDecode(valor.Groups[1].Value);
        }
        // POST sin querystring: el formulario debe transportar el contexto de navegación.
        var response = await client.PostAsync("/backoffice/deposito/recepcion", new FormUrlEncodedContent(campos));
        response.EnsureSuccessStatusCode();
        var resultado = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Bulto recibido", resultado);
        Assert.Contains($"href=\"{detalleUrl}\"", resultado);
        var vuelta = WebUtility.HtmlDecode(await client.GetStringAsync(detalleUrl));
        Assert.Contains($"href=\"/backoffice/envios?{parametros}\"", vuelta);
        Assert.DoesNotContain($"aria-label=\"Recepcionar bulto {envio.Numero}-1\"", vuelta);
    }

    [Fact]
    public async Task Transito_no_ofrece_recepcion_inicial_aunque_permite_la_transicion_de_reintegro()
    {
        var client = factory.CreateClient();
        var envio = await CrearAsync(client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var entidad = (await db.Envios.FindAsync(envio.Id))!;
        foreach (var estado in new[] { EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito })
        {
            entidad.Transicionar(estado, OrigenEvento.Backoffice, null, DateTimeOffset.UtcNow);
        }
        await db.SaveChangesAsync();
        Assert.True(TablaTransiciones.Permite(EstadoEnvio.EnTransito, EstadoEnvio.EnDeposito));
        Assert.DoesNotContain("Recepcionar</a>", await client.GetStringAsync($"/backoffice/envios/{envio.Numero}"));
    }

    [Theory]
    [InlineData("/backoffice/deposito/recepcion")]
    [InlineData("/backoffice/deposito/recepcion?CodigoBulto=")]
    public async Task Abrir_sin_codigo_no_muestra_error_pero_registrar_lo_exige(string url)
    {
        var client = factory.CreateClient();
        var formulario = await client.GetStringAsync(url);
        Assert.DoesNotContain("alert-danger", formulario);
        Assert.DoesNotContain("The CodigoBulto field is required.", formulario);
        var token = Regex.Match(Input(formulario, "__RequestVerificationToken"), "value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var response = await client.PostAsync("/backoffice/deposito/recepcion", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["CodigoBulto"] = "",
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            }));
        response.EnsureSuccessStatusCode();
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Escaneá o escribí el código del bulto.", html);
        Assert.DoesNotContain("The CodigoBulto field is required.", html);
        Assert.DoesNotContain("Bulto recibido", html);
    }

    [Fact]
    public async Task Precarga_solo_codigo_y_actualiza_acciones_al_recibir_cada_bulto()
    {
        var client = factory.CreateClient();
        var envio = await CrearAsync(client);
        var detalleUrl = $"/backoffice/envios/{envio.Numero}";
        var primero = $"/backoffice/deposito/recepcion?CodigoBulto={envio.Numero}-1";
        var segundo = $"/backoffice/deposito/recepcion?CodigoBulto={envio.Numero}-2";
        var detalle = await client.GetStringAsync(detalleUrl);
        Assert.Contains($"href=\"{primero}\"", detalle);
        Assert.Contains($"href=\"{segundo}\"", detalle);

        // Aunque se agreguen medidas a la URL, el formulario sólo toma el código por GET.
        var formulario = await client.GetStringAsync(primero + "&PesoKg=99&LargoCm=99&AnchoCm=99&AltoCm=99");
        Assert.Contains($"value=\"{envio.Numero}-1\"", Input(formulario, "CodigoBulto"));
        foreach (var campo in new[] { "PesoKg", "LargoCm", "AnchoCm", "AltoCm" })
        {
            var valor = Regex.Match(Input(formulario, campo), "value=\"([^\"]*)\"");
            Assert.True(!valor.Success || valor.Groups[1].Value == "");
        }
        Assert.Contains($"href=\"{detalleUrl}\"", formulario);
        // Abrir el formulario no registra la recepción.
        Assert.Contains($"href=\"{primero}\"", await client.GetStringAsync(detalleUrl));

        var token = Regex.Match(Input(formulario, "__RequestVerificationToken"), "value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var recibido = await client.PostAsync("/backoffice/deposito/recepcion", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["CodigoBulto"] = $"{envio.Numero}-1",
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            }));
        recibido.EnsureSuccessStatusCode();
        var resultado = WebUtility.HtmlDecode(await recibido.Content.ReadAsStringAsync());
        Assert.Contains("Bulto recibido", resultado);
        Assert.Contains($"href=\"{detalleUrl}\"", resultado);
        foreach (var campo in new[] { "CodigoBulto", "PesoKg", "LargoCm", "AnchoCm", "AltoCm" })
        {
            Assert.DoesNotContain("value=\"99\"", Input(resultado, campo));
        }

        detalle = await client.GetStringAsync(detalleUrl);
        Assert.DoesNotContain($"href=\"{primero}\"", detalle);
        Assert.Contains($"href=\"{segundo}\"", detalle);
        Assert.Contains("Recibido", detalle);
        Assert.Contains("Pendiente", detalle);

        var ultimo = await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest($"{envio.Numero}-2"));
        ultimo.EnsureSuccessStatusCode();
        detalle = WebUtility.HtmlDecode(await client.GetStringAsync(detalleUrl));
        Assert.DoesNotContain("Recepcionar</a>", detalle);
        Assert.Contains("En depósito", detalle);
        Assert.DoesNotContain(">Pendiente</td>", detalle);
    }

    [Fact]
    public async Task Un_envio_cancelado_no_ofrece_recepcion_y_codigos_inexistentes_o_ajenos_no_precargan()
    {
        var client = factory.CreateClient();
        var envio = await CrearAsync(client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var entidad = await db.Envios.FindAsync(envio.Id);
        entidad!.Transicionar(EstadoEnvio.Cancelado, OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var ajeno = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), $"ENV-AJENO-{Guid.NewGuid():N}",
            new Destinatario("Ajeno", "099000000"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11200"),
            [new DatosBulto(2, 30, 20, 10, 200)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow);
        await using (var dbAjeno = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(ajeno.OperadorId, ajeno.ComercioId)))
        {
            dbAjeno.Envios.Add(ajeno);
            await dbAjeno.SaveChangesAsync();
        }

        var detalle = await client.GetStringAsync($"/backoffice/envios/{envio.Numero}");
        Assert.DoesNotContain("Recepcionar</a>", detalle);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/backoffice/deposito/recepcion?CodigoBulto={ajeno.Numero}-1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/backoffice/deposito/recepcion?CodigoBulto=NO-EXISTE")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/backoffice/deposito/recepcion")).StatusCode);
    }

    private static string Input(string html, string nombre)
    {
        var input = Regex.Match(html, $"<input\\b[^>]*\\bname=\"{Regex.Escape(nombre)}\"[^>]*>");
        Assert.True(input.Success, $"No se encontró el campo {nombre}.");
        return input.Value;
    }

    private static async Task<CrearEnvioResponse> CrearAsync(HttpClient client)
    {
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Recepción Demo", "099000000", null, null),
            new DireccionRequest("Calle", "1", "Montevideo", "Montevideo", "11200", null),
            [new BultoRequest(2, 30, 20, 10), new BultoRequest(3, 40, 30, 20)]));
        creado.EnsureSuccessStatusCode();
        return (await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
    }
}
