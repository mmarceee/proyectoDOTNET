using System.Collections.Concurrent;
using System.Diagnostics;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.IntegrationTests.Seguimiento;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Logistica.IntegrationTests.BuildingBlocks;

// ADR-0003, sección 2.9: una operación se sigue en una sola traza desde el request de la API, pasando por la
// publicación del outbox en RabbitMQ, hasta el consumidor del Worker.
public sealed class TrazasMensajeriaTests(PostgresApiFactory factory, RabbitMqFixture rabbit)
    : IClassFixture<PostgresApiFactory>, IClassFixture<RabbitMqFixture>
{
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task El_consumidor_del_Worker_continua_la_traza_del_request_a_traves_de_la_publicacion()
    {
        // Hace de OpenTelemetry: escucha la fuente de la mensajería en la API y en el Worker de la prueba.
        var actividades = new ConcurrentBag<Activity>();
        using var oyente = new ActivityListener
        {
            ShouldListenTo = fuente => fuente.Name == TrazasMensajeria.Fuente,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = actividades.Add,
        };
        ActivitySource.AddActivityListener(oyente);

        // Sin RabbitMQ la API no arranca su publicador: la prueba lo ejecuta a mano.
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:RabbitMQ", ""));
        using var client = api.CreateClient(); // Inicializa y migra la API.

        // El request que despachó la ruta: AgregarAlOutbox guarda su traza (Activity.Current) en la fila.
        ActivityTraceId traza;
        ActivitySpanId spanDelRequest;
        using (var request = new Activity("Despachar ruta").Start())
        {
            traza = request.TraceId;
            spanDelRequest = request.SpanId;
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            db.AgregarAlOutbox(RutaDespachada.Tipo, new RutaDespachada(Guid.NewGuid(), Repartidor, new(2042, 1, 1), 1, null),
                DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var publicador = new PublicadorOutbox(api.Services.GetRequiredService<IServiceScopeFactory>(),
            api.Services.GetRequiredService<IConfiguration>(), api.Services.GetServices<SuscripcionIntegracion>(),
            api.Services.GetRequiredService<ILogger<PublicadorOutbox>>());
        await using var conexion = await new ConnectionFactory { Uri = new(rabbit.ConnectionString) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true));
        await RabbitMqTransport.DeclararAsync(canal, api.Services.GetServices<SuscripcionIntegracion>(), default);
        await publicador.PublicarPendientesAsync(canal, default);

        using var worker = CrearWorker();
        await worker.StartAsync();
        // RutaDespachada la procesan dos consumidores (el aviso al repartidor y el tablero de CU-66): se verifica el del
        // aviso, eligiéndolo por su cola.
        var procesamiento = await EsperarAsync(() =>
            actividades.SingleOrDefault(a => a.Kind == ActivityKind.Consumer && a.TraceId == traza
                && (string?)a.GetTagItem("messaging.destination.name") == NotificarRutaDespachada.Cola));
        await worker.StopAsync();

        var publicacion = Assert.Single(actividades, a => a.Kind == ActivityKind.Producer && a.TraceId == traza);
        Assert.Equal(spanDelRequest, publicacion.ParentSpanId);
        Assert.Equal(publicacion.SpanId, procesamiento.ParentSpanId);
        Assert.Equal(ActivityStatusCode.Unset, procesamiento.Status);
        Assert.Equal(RutaDespachada.Tipo, procesamiento.GetTagItem("logistica.evento.tipo"));
    }

    private IHost CrearWorker()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = factory.ConnectionString,
            ["ConnectionStrings:RabbitMQ"] = rabbit.ConnectionString,
        });
        builder.Services.AddScoped<InquilinoMensaje>();
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<InquilinoMensaje>());
        builder.Services.AddSeguimientoModule(builder.Configuration);
        builder.Services.AddHostedService<ConsumidorRabbitMq>();
        return builder.Build();
    }

    private static async Task<T> EsperarAsync<T>(Func<T?> consulta) where T : class
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        do
        {
            if (consulta() is { } resultado) return resultado;
            await Task.Delay(200);
        } while (DateTime.UtcNow < limite);
        throw new TimeoutException("El Worker no procesó el mensaje dentro del plazo de la prueba.");
    }
}
