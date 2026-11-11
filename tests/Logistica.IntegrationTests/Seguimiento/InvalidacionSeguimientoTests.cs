using System.Diagnostics;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.Http.Contracts.Planificacion;
using Logistica.IntegrationTests.Identidad;
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

    [Fact]
    public async Task RF34_invalida_por_asignacion_y_retorno_individual_con_API_Worker_RabbitMQ_y_Valkey_separados()
    {
        using var api = factory.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:RabbitMQ", rabbit.ConnectionString)
            .UseSetting("ConnectionStrings:Valkey", valkey.ConnectionString)
            .UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")
            .UseSetting("Seguimiento:ConsultasPorMinuto", "1000"));
        using var client = api.CreateClient();
        using var worker = CrearWorker(factory.ConnectionString, rabbit.ConnectionString, valkey: valkey.ConnectionString);
        await worker.StartAsync();
        var envio = await CrearEnvioAsync(client, 3);
        var token = api.Services.GetRequiredService<TokenSeguimiento>().Generar(DatosIniciales.OperadorDemoId, envio.Id);
        Assert.Equal("Recibimos tu envío", (await ConsultarAsync(client, token)).Estado);
        for (var i = 1; i <= 3; i++)
            (await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest($"{envio.Numero}-{i}"))).EnsureSuccessStatusCode();
        await EsperarAsync(async () => (await ConsultarAsync(client, token)).Bultos.All(b => b.Estado == "En el depósito") ? "ok" : null,
            Plazo, "la recepción se refleja antes de iniciar el escenario RF34");
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var disponibles = (await client.GetFromJsonAsync<EnviosDisponiblesResponse>(
            $"/api/planificacion/envios-disponibles?fecha={fecha:yyyy-MM-dd}&texto={envio.Numero}"))!;
        var bultos = Assert.Single(disponibles.Items).Bultos.Take(2).ToArray();
        var asignacion = await client.PostAsJsonAsync("/api/planificacion/rutas", new CrearRutaRequest(fecha,
            DatosIniciales.RepartidorDemoId, Guid.Parse("51111111-1111-1111-1111-111111111111"), [envio.Id],
            bultos.Select(b => b.Id).ToArray(), Guid.NewGuid(), bultos.ToDictionary(b => b.Id, b => b.RevisionCustodia)));
        Assert.True(asignacion.IsSuccessStatusCode, await asignacion.Content.ReadAsStringAsync());
        var ruta = (await asignacion.Content.ReadFromJsonAsync<RutaResponse>())!;
        await EsperarAsync(async () => (await ConsultarAsync(client, token)).Bultos.Count(b => b.Estado == "Preparando la entrega") == 2 ? "ok" : null,
            Plazo, "el evento interno por bulto invalida el progreso aunque no cambie el estado global");

        using var deposito = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        var pedido = new ConciliarBultosRequest(Guid.NewGuid(), ruta.Id, ruta.Revision,
            [new(bultos[0].Id, bultos[0].RevisionCustodia)], true, "Presencia física del bulto confirmada en depósito");
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, "/api/deposito/custodia/conciliaciones") { Content = JsonContent.Create(pedido) };
        solicitud.Headers.Add("RequestVerificationToken", await Sesion.TokenAsync(deposito, "/backoffice/custodia"));
        var conciliacion = await deposito.SendAsync(solicitud);
        Assert.True(conciliacion.IsSuccessStatusCode, await conciliacion.Content.ReadAsStringAsync());
        await EsperarAsync(async () => (await ConsultarAsync(client, token)).Bultos.Count(b => b.Estado == "En el depósito") == 2 ? "ok" : null,
            Plazo, "el retorno individual confirmado invalida por Outbox y Worker antes de vencer el TTL");
        var final = await ConsultarAsync(client, token);
        Assert.Equal("0 de 3 bultos entregados", final.Avance);
        Assert.Single(final.Bultos, b => b.Estado == "Preparando la entrega");
        salida.WriteLine("RF34: asignación de 2/3 y retorno de 1/3 observados por el circuito distribuido real.");
        await worker.StopAsync();
    }

    private static async Task<CrearEnvioResponse> CrearEnvioAsync(HttpClient client, int cantidad = 1)
    {
        var respuesta = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", Email: null, Documento: null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", Referencia: null),
            Enumerable.Repeat(new BultoRequest(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10), cantidad).ToArray()));
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
    }

    private static async Task<SeguimientoPublicoResponse> ConsultarAsync(HttpClient client, string token) =>
        (await client.GetFromJsonAsync<SeguimientoPublicoResponse>($"/api/seguimiento/{token}"))!;
}
