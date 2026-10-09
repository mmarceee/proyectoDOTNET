using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

public sealed class DetalleEnvioPaginaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task El_listado_enlaza_al_detalle_y_permite_volver_con_los_filtros()
    {
        var client = factory.CreateClient();
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Detalle", "099123456", null, null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10), new BultoRequest(3, 40, 30, 20)]));
        creado.EnsureSuccessStatusCode();
        var envio = (await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        var listado = await client.GetStringAsync($"/backoffice/envios?Pagina=1&TamanoPagina=5&Estado=Admitido&Texto={envio.Numero}");
        var enlace = Regex.Match(listado, "href=\"([^\"]+)\"\\s+aria-label=\"Ver detalle del env[^\"]+\"");
        Assert.True(enlace.Success);
        var urlDetalle = WebUtility.HtmlDecode(enlace.Groups[1].Value);
        Assert.Equal($"/backoffice/envios/{envio.Numero}?TamanoPagina=5&Estado=Admitido&Texto={envio.Numero}", urlDetalle);

        var response = await client.GetAsync(urlDetalle);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Envío {envio.Numero}", html);
        Assert.Contains("Ana Detalle", html);
        Assert.Contains("Av. Italia 1234", html);
        Assert.Contains($"{envio.Numero}-1", html);
        Assert.Contains($"{envio.Numero}-2", html);
        Assert.Contains("Tarifa total", html);
        Assert.Contains("Historial de eventos", html);
        Assert.Contains("Alta del envío", html);
        Assert.Contains("PortalComercio", html);
        Assert.Contains("No se registró una versión tarifaria", html);
        Assert.Contains("No hay intentos de entrega registrados", html);
        Assert.Contains("No hay incidencias registradas", html);
        Assert.Contains("No hay devolución registrada", html);
        Assert.Contains($"href=\"/backoffice/envios?TamanoPagina=5&Estado=Admitido&Texto={envio.Numero}\"", html);
    }

    [Fact]
    public async Task Los_valores_predeterminados_no_se_agregan_a_la_url_del_detalle()
    {
        var numero = await CrearEnvioVisibleAsync();
        var listado = await factory.CreateClient().GetStringAsync("/backoffice/envios?Pagina=1&TamanoPagina=20");
        var enlace = Regex.Match(listado, $"href=\"([^\"]+)\"\\s+aria-label=\"Ver detalle del env[^\"]*{Regex.Escape(numero)}\"");

        Assert.True(enlace.Success);
        Assert.Equal($"/backoffice/envios/{numero}", WebUtility.HtmlDecode(enlace.Groups[1].Value));
        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync(
            $"/backoffice/envios/{numero}"));

        Assert.Contains("href=\"/backoffice/envios\" class=\"btn btn-outline-secondary\">Volver al listado", html);
    }

    [Fact]
    public async Task El_detalle_conserva_pagina_fechas_y_texto_con_caracteres_especiales_al_volver()
    {
        var numero = await CrearEnvioVisibleAsync();
        const string parametros = "Pagina=3&TamanoPagina=5&Estado=Admitido&FechaDesde=2026-10-01&FechaHasta=2026-10-15&Texto=Ana%20%26%20P%C3%A9rez";
        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync(
            $"/backoffice/envios/{numero}?{parametros}"));

        Assert.Contains($"href=\"/backoffice/envios?{parametros}\"", html);
    }

    [Fact]
    public async Task Un_envio_inexistente_o_de_otro_operador_responde_404()
    {
        using var scope = factory.Services.CreateScope();
        var numero = $"ENV-{Guid.NewGuid():N}";
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), numero,
            new Destinatario("Ajeno", "099123456"),
            new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 0)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow);

        // Es de otro operador: se guarda con un contexto de ese operador, como lo haría él.
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(
            scope.ServiceProvider, new InquilinoFijo(envio.OperadorId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/backoffice/envios/{numero}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/backoffice/envios/INEXISTENTE-{Guid.NewGuid():N}")).StatusCode);
    }

    private async Task<string> CrearEnvioVisibleAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<Logistica.SharedKernel.ICurrentTenant>();
        var envio = Envio.Crear(tenant.OperadorId!.Value, tenant.ComercioId ?? Guid.NewGuid(), $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana Detalle", "099123456"),
            new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 0)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow);
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio.Numero;
    }
}
