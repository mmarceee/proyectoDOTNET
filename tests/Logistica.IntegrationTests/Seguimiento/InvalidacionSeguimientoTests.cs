using System.Diagnostics;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.IntegrationTests.BuildingBlocks;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Logistica.IntegrationTests.Resiliencia.Interrupciones;

namespace Logistica.IntegrationTests.Seguimiento;

// ADR-0006, sección 2.3, de punta a punta: la API guarda el seguimiento en Valkey; al recibir el envío en el depósito
// (T2), el evento sale por el outbox, viaja por RabbitMQ y el Worker borra la clave. La consulta siguiente ya muestra el
// estado nuevo, mucho antes del minuto de expiración. API y Worker son hosts separados que sólo comparten PostgreSQL,
// RabbitMQ y Valkey, como en Docker Compose.
[Collection(ContenedoresPropios.Nombre)]
public sealed class InvalidacionSeguimientoTests(
    PostgresApiFactory factory, RabbitMqFixture rabbit, ValkeyFixture valkey, ITestOutputHelper salida)
    : IClassFixture<PostgresApiFactory>, IClassFixture<RabbitMqFixture>, IClassFixture<ValkeyFixture>
{
    // Muy por debajo de la expiración de 60 s: si el estado nuevo aparece antes, fue por la invalidación.
    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Al_recibir_el_envio_en_el_deposito_el_Worker_borra_el_seguimiento_de_la_cache()
    {
        using var api = factory.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:RabbitMQ", rabbit.ConnectionString)
            .UseSetting("ConnectionStrings:Valkey", valkey.ConnectionString)
            .UseSetting("Seguimiento:ConsultasPorMinuto", "1000"));
        using var client = api.CreateClient();
        using var worker = CrearWorker(factory.ConnectionString, rabbit.ConnectionString, valkey: valkey.ConnectionString);
        await worker.StartAsync();

        var envio = await CrearEnvioAsync(client);
        var token = api.Services.GetRequiredService<TokenSeguimiento>().Generar(DatosIniciales.OperadorDemoId, envio.Id);

        // Antes: la destinataria consulta y el seguimiento queda en Valkey por un minuto.
        Assert.Equal("Recibimos tu envío", (await ConsultarAsync(client, token)).Estado);

        // T2: Depósito recibe el único bulto y Envíos pasa a EnDeposito, con su evento en el outbox.
        var recepcion = await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest($"{envio.Numero}-1"));
        recepcion.EnsureSuccessStatusCode();
        Assert.True((await recepcion.Content.ReadFromJsonAsync<RecibirBultoResponse>())!.EnvioEnDeposito);

        // Después: outbox, RabbitMQ y Worker borran la clave, y la consulta siguiente arma el estado nuevo.
        var reloj = Stopwatch.StartNew();
        await EsperarAsync(async () => (await ConsultarAsync(client, token)).Estado == "En el depósito" ? "ok" : null,
            Plazo, "el seguimiento muestra En el depósito antes de que venza la caché");
        salida.WriteLine($"El seguimiento mostró el estado nuevo {reloj.Elapsed.TotalSeconds:0.0} s después de la recepción.");

        await worker.StopAsync();
    }

    private static async Task<CrearEnvioResponse> CrearEnvioAsync(HttpClient client)
    {
        var respuesta = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", Email: null, Documento: null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", Referencia: null),
            [new BultoRequest(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10)]));
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
    }

    private static async Task<SeguimientoPublicoResponse> ConsultarAsync(HttpClient client, string token) =>
        (await client.GetFromJsonAsync<SeguimientoPublicoResponse>($"/api/seguimiento/{token}"))!;
}
