using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Microsoft.AspNetCore.Hosting;

namespace Logistica.IntegrationTests.Envios;

public sealed class DocumentacionApiTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string OpenApi = "/api/openapi/v1.json";
    private const string Envio = "/api/v1/envios";
    private const string Consola = "/api/v1/pruebas/envios";

    [Fact]
    public async Task Publica_la_pagina_y_un_OpenApi_versionado_con_solo_operaciones_publicas()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("ApiPublica:PruebaPorMinuto", "13"));
        using var cliente = api.CreateClient(Sesion.Opciones);

        using var pagina = await cliente.GetAsync("/api/docs");
        pagina.EnsureSuccessStatusCode();
        Assert.Equal("text/html", pagina.Content.Headers.ContentType?.MediaType);
        Assert.Contains("/api/docs/docs.js", await pagina.Content.ReadAsStringAsync());
        Assert.Contains("script-src 'self'", Assert.Single(pagina.Headers.GetValues("Content-Security-Policy")));
        using var script = await cliente.GetAsync("/api/docs/docs.js");
        script.EnsureSuccessStatusCode();
        using var estilos = await cliente.GetAsync("/api/docs/docs.css");
        estilos.EnsureSuccessStatusCode();

        using var json = (await cliente.GetFromJsonAsync<JsonDocument>(OpenApi))!;
        var raiz = json.RootElement;
        Assert.StartsWith("3.", raiz.GetProperty("openapi").GetString());
        Assert.Equal("v1", raiz.GetProperty("info").GetProperty("version").GetString());
        Assert.Contains("13 en prueba", raiz.GetProperty("info").GetProperty("description").GetString());
        var rutas = raiz.GetProperty("paths");
        Assert.Equal(new[] { Envio, Envio + "/{numero}", Consola + "/{numero}/seguimiento" }.Order(),
            rutas.EnumerateObject().Select(r => r.Name).Order());
        var clave = raiz.GetProperty("components").GetProperty("securitySchemes").GetProperty("ClaveApi");
        Assert.Equal("apiKey", clave.GetProperty("type").GetString());
        Assert.Equal("header", clave.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", clave.GetProperty("name").GetString());
        Assert.True(raiz.GetProperty("security")[0].TryGetProperty("ClaveApi", out _));

        var alta = rutas.GetProperty(Envio).GetProperty("post");
        Assert.Equal("CrearEnvioV1", alta.GetProperty("operationId").GetString());
        var respuestas = alta.GetProperty("responses");
        foreach (var codigo in new[] { "200", "201", "400", "401", "422", "429" })
            Assert.True(respuestas.TryGetProperty(codigo, out _), $"Falta la respuesta {codigo}.");
        Assert.True(respuestas.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));

        var lista = rutas.GetProperty(Envio).GetProperty("get");
        Assert.Contains(lista.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "estado");
    }

    [Fact]
    public async Task El_ejemplo_OpenApi_funciona_en_consola_y_sus_rutas_rechazan_claves_de_produccion()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("Seguimiento:ClaveToken", Convert.ToBase64String(new byte[32])));
        using var portal = await CrearEnvioApiTests.PortalAsync(api);
        var clavePrueba = await CrearEnvioApiTests.CrearClaveAsync(portal, "Prueba");
        var claveReal = await CrearEnvioApiTests.CrearClaveAsync(portal, "Produccion");
        using var sinClave = api.CreateClient(Sesion.Opciones);
        using var pruebas = api.CreateClient(Sesion.Opciones);
        pruebas.DefaultRequestHeaders.Add("X-Api-Key", clavePrueba.Secreto);
        using var produccion = api.CreateClient(Sesion.Opciones);
        produccion.DefaultRequestHeaders.Add("X-Api-Key", claveReal.Secreto);

        using var documento = (await sinClave.GetFromJsonAsync<JsonDocument>(OpenApi))!;
        var ejemplo = documento.RootElement.GetProperty("paths").GetProperty(Envio).GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
        var cuerpo = ejemplo.Deserialize<CrearEnvioApiRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        cuerpo = cuerpo with { ReferenciaComercio = Guid.NewGuid().ToString("N") };

        using var noAutorizada = await sinClave.GetAsync(Consola);
        Assert.Equal(HttpStatusCode.Unauthorized, noAutorizada.StatusCode);
        using var altaReal = await produccion.PostAsJsonAsync(Consola, cuerpo);
        Assert.Equal(HttpStatusCode.Forbidden, altaReal.StatusCode);

        using var alta = await pruebas.PostAsJsonAsync(Consola, cuerpo);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var creada = (await alta.Content.ReadFromJsonAsync<AltaEnvioApiResponse>())!;
        var listado = (await pruebas.GetFromJsonAsync<PaginaEnviosApiResponse>(
            $"{Consola}?referenciaComercio={cuerpo.ReferenciaComercio}"))!;
        Assert.Equal(creada.Numero, Assert.Single(listado.Envios).Numero);
        var detalle = (await pruebas.GetFromJsonAsync<DetalleEnvioApiResponse>($"{Consola}/{creada.Numero}"))!;
        Assert.Equal(creada.Numero, detalle.Envio.Numero);
        using var consultaReal = await produccion.GetAsync($"{Consola}/{creada.Numero}");
        Assert.Equal(HttpStatusCode.Forbidden, consultaReal.StatusCode);
        using var listaReal = await produccion.GetAsync(Consola);
        Assert.Equal(HttpStatusCode.Forbidden, listaReal.StatusCode);
        using var produccionNormal = await produccion.GetAsync($"{Envio}/{creada.Numero}");
        Assert.Equal(HttpStatusCode.NotFound, produccionNormal.StatusCode);
    }
}
