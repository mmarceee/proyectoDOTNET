using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Logistica.IntegrationTests.Resiliencia.Interrupciones;

namespace Logistica.IntegrationTests.Resiliencia;

// Caso 2 de docs/pruebas-de-resiliencia.md: RabbitMQ detenido con la API y el Worker corriendo. Los envíos se siguen
// creando, porque el evento se guarda en el outbox en la misma transacción (ADR-0003); queda pendiente mientras no hay
// broker y, cuando vuelve, el publicador de la API y el consumidor del Worker se reconectan solos y el evento llega.
[Collection(ContenedoresPropios.Nombre)]
public sealed class RabbitMqCaidoTests(PostgresApiFactory factory, ITestOutputHelper salida) : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(60);
    private ContenedorInterrumpible rabbit = null!;

    private string Conexion => $"amqp://guest:guest@{rabbit.Host}:{rabbit.Puerto(5672)}/";

    public async Task InitializeAsync()
    {
        rabbit = await ContenedorInterrumpible.IniciarAsync("rabbitmq:4.1-management", [5672], b => b
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete")));
    }

    public async Task DisposeAsync() => await rabbit.DisposeAsync();

    [Fact]
    public async Task Sin_broker_los_eventos_esperan_en_el_outbox_y_llegan_al_Worker_cuando_vuelve()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:RabbitMQ", Conexion));
        using var portal = await PortalAsync(api);
        using var worker = CrearWorker(factory.ConnectionString, Conexion);
        await worker.StartAsync();

        // Antes: el evento de un envío nuevo se publica y el Worker lo procesa.
        var antes = await CrearAsync(portal);
        await EsperarAsync(() => ProcesadoPorElWorkerAsync(api, antes), Plazo, "el Worker procesa el alta con el broker arriba");

        await rabbit.DetenerAsync();

        // Durante: el envío se crea igual, y su evento queda pendiente en el outbox, sin perderse.
        var reloj = Stopwatch.StartNew();
        var durante = await CrearAsync(portal);
        salida.WriteLine($"Envío creado sin broker en {reloj.ElapsedMilliseconds} ms.");
        await Task.Delay(TimeSpan.FromSeconds(5)); // más que el intervalo del publicador (2 s)
        var pendiente = await FilaOutboxAsync(api.Services, durante);
        Assert.Null(pendiente.PublicadoEn);

        await rabbit.ReanudarAsync();
        reloj.Restart();

        // Después: sin reiniciar la API ni el Worker, el evento se publica y el Worker lo procesa.
        await EsperarAsync(async () => (await FilaOutboxAsync(api.Services, durante)).PublicadoEn is not null ? "ok" : null,
            Plazo, "el publicador de la API publica el evento pendiente");
        salida.WriteLine($"Evento pendiente publicado {reloj.Elapsed.TotalSeconds:0.0} s después de que el broker aceptó conexiones.");
        await EsperarAsync(() => ProcesadoPorElWorkerAsync(api, durante), Plazo,
            "el consumidor del Worker procesa el evento pendiente");
        salida.WriteLine($"Evento procesado por el Worker {reloj.Elapsed.TotalSeconds:0.0} s después de que volvió el broker.");
        await worker.StopAsync();
    }

    private static async Task<Guid> CrearAsync(HttpClient portal)
    {
        var respuesta = await CrearEnvioAsync(portal);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!.Id;
    }

    // La fila del outbox de Envíos con el alta del envío.
    private static async Task<OutboxMessage> FilaOutboxAsync(IServiceProvider services, Guid envio)
    {
        await using var scope = services.CreateAsyncScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        var altas = await db.Outbox.AsNoTracking().Where(m => m.Tipo == EnvioAdmitido.Tipo).ToListAsync();
        return altas.Single(m => JsonSerializer.Deserialize<EnvioAdmitido>(m.Contenido)!.EnvioId == envio);
    }

    // El inbox de Seguimiento guarda el MessageId (el Id de la fila del outbox) de cada mensaje procesado.
    private static async Task<string?> ProcesadoPorElWorkerAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> api, Guid envio)
    {
        var fila = await FilaOutboxAsync(api.Services, envio);
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(new MensajeIntegracion(fila.Id, fila.Tipo,
            fila.OperadorId, fila.ComercioId, fila.OcurridoEn, null, fila.Contenido));
        var db = scope.ServiceProvider.GetRequiredService<SeguimientoDbContext>();
        return await db.Inbox.AnyAsync(m => m.MessageId == fila.Id) ? "ok" : null;
    }
}
