using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Logistica.IntegrationTests.Seguimiento;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly IContainer broker = new ContainerBuilder("rabbitmq:4.1-management")
        .WithPortBinding(5672, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672)).Build();
    public string ConnectionString => $"amqp://guest:guest@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}/";
    public Task InitializeAsync() => broker.StartAsync();
    public Task DisposeAsync() => broker.DisposeAsync().AsTask();
}

public class AvisosRutaTests(PostgresApiFactory factory, RabbitMqFixture rabbit)
    : IClassFixture<PostgresApiFactory>, IClassFixture<RabbitMqFixture>
{
    private static readonly Guid Operador = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static int dia;

    [Fact]
    public async Task Despacho_real_publica_aviso_persistente_sin_cambiar_envio_y_no_duplica_al_reentregar()
    {
        using var api = factory.WithWebHostBuilder(b => b
            .UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")
            .UseSetting("ConnectionStrings:RabbitMQ", rabbit.ConnectionString));
        using var client = api.CreateClient(Sesion.Opciones);
        var (ruta, envio) = await CrearRutaAsync(client);
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", await Sesion.TokenAsync(client, $"/backoffice/rutas/{ruta.Id}"));
        var respuesta = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho", new DespacharRutaRequest(ruta.Revision));
        respuesta.EnsureSuccessStatusCode();
        var despachada = (await respuesta.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal("Despachada", despachada.Estado);
        await using (var scope = api.Services.CreateAsyncScope())
            Assert.Equal("AsignadoARuta", (await scope.ServiceProvider.GetRequiredService<EnviosDbContext>()
                .Envios.SingleAsync(e => e.Id == envio.Id)).Estado.ToString());

        // La publicación funciona aunque el Worker todavía no esté iniciado: la cola es durable.
        var mensaje = await EsperarAsync(async () =>
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            return (await db.Outbox.AsNoTracking().Where(m => m.Tipo == RutaDespachada.Tipo && m.PublicadoEn != null).ToListAsync())
                .SingleOrDefault(m => JsonDocument.Parse(m.Contenido).RootElement.GetProperty("RutaId").GetGuid() == ruta.Id);
        });
        var sobre = new MensajeIntegracion(mensaje.Id, mensaje.Tipo, mensaje.OperadorId, mensaje.ComercioId,
            mensaje.OcurridoEn, mensaje.TraceParent, mensaje.Contenido);

        using var repartidor = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(repartidor, "repartidor@operador-demo.uy");
        using (var worker = CrearWorker())
        {
            await worker.StartAsync();
            var aviso = await EsperarAsync(async () =>
                (await repartidor.GetFromJsonAsync<List<AvisoRutaResponse>>("/api/repartidor/avisos"))!.SingleOrDefault(a => a.RutaId == ruta.Id));
            Assert.Equal(ruta.Fecha, aviso.FechaRuta);
            Assert.Equal(despachada.Revision, aviso.Revision);
            await worker.StopAsync();
        }

        await using var conexion = await new ConnectionFactory { Uri = new(rabbit.ConnectionString) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true));
        await canal.BasicPublishAsync(RabbitMqTransport.Exchange, mensaje.Tipo, true,
            RabbitMqTransport.Propiedades(sobre), Encoding.UTF8.GetBytes(mensaje.Contenido));
        // Reiniciar el consumidor no pierde el aviso ni olvida que procesó el MessageId.
        using (var worker = CrearWorker())
        {
            await worker.StartAsync();
            await EsperarAsync(async () => (await canal.QueueDeclarePassiveAsync(NotificarRutaDespachada.Cola)).MessageCount == 0 ? new object() : null);
            await worker.StopAsync();
        }
        await using var final = api.Services.CreateAsyncScope();
        var seguimiento = final.ServiceProvider.GetRequiredService<SeguimientoDbContext>();
        Assert.Single(await seguimiento.Avisos.Where(a => a.RutaId == ruta.Id).ToListAsync());
        Assert.Single(await seguimiento.Inbox.Where(m => m.MessageId == mensaje.Id).ToListAsync());
        var consulta = await repartidor.GetAsync("/api/repartidor/avisos");
        Assert.True(consulta.Headers.CacheControl?.NoStore);
        var planificacion = final.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var entidad = await planificacion.Rutas.SingleAsync(r => r.Id == ruta.Id);
        entidad.Finalizar();
        await planificacion.SaveChangesAsync();
    }

    [Fact]
    public async Task Avisos_exigen_repartidor_y_aislan_operador_y_repartidor_sin_aceptar_ids_del_cliente()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:RabbitMQ", ""));
        using var client = api.CreateClient(Sesion.Opciones);
        var propia = Guid.NewGuid(); var deOtroRepartidor = Guid.NewGuid(); var deOtroOperador = Guid.NewGuid();
        await ProcesarAsync(api.Services, CrearMensaje(propia, Operador, Repartidor));
        await ProcesarAsync(api.Services, CrearMensaje(deOtroRepartidor, Operador, Guid.NewGuid()));
        await ProcesarAsync(api.Services, CrearMensaje(deOtroOperador, Guid.NewGuid(), Repartidor));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/repartidor/avisos")).StatusCode);
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/repartidor/avisos")).StatusCode);
        using var repartidor = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(repartidor, "repartidor@operador-demo.uy");
        var avisos = (await repartidor.GetFromJsonAsync<List<AvisoRutaResponse>>($"/api/repartidor/avisos?repartidorId={Guid.NewGuid()}&operadorId={Guid.NewGuid()}"))!;
        Assert.Contains(avisos, a => a.RutaId == propia);
        Assert.DoesNotContain(avisos, a => a.RutaId == deOtroRepartidor || a.RutaId == deOtroOperador);
        Assert.Equal(HttpStatusCode.BadRequest, (await repartidor.GetAsync("/api/repartidor/avisos?pagina=0")).StatusCode);
        Assert.Empty((await repartidor.GetFromJsonAsync<List<AvisoRutaResponse>>("/api/repartidor/avisos?pagina=10000"))!);
    }

    [Fact]
    public async Task Consumidor_rechaza_inquilino_ausente_y_evento_incompleto_sin_guardar_inbox_ni_aviso()
    {
        using var client = factory.CreateClient(); // Inicializa y migra la API.
        var incompleto = CrearMensaje(Guid.NewGuid(), Operador, Repartidor) with { Contenido = "{}" };
        await Assert.ThrowsAsync<InvalidDataException>(() => ProcesarAsync(factory.Services, incompleto));
        var sinOperador = incompleto with { OperadorId = Guid.Empty };
        await Assert.ThrowsAsync<InvalidDataException>(() => ProcesarAsync(factory.Services, sinOperador));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SeguimientoDbContext>();
        Assert.False(await db.Inbox.AnyAsync(m => m.MessageId == incompleto.Id));
    }

    [Fact]
    public async Task Publicacion_fallida_conserva_outbox_y_se_recupera_sin_perder_el_aviso()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:RabbitMQ", ""));
        using var client = api.CreateClient();
        var mensaje = CrearMensaje(Guid.NewGuid(), Operador, Repartidor);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            db.AgregarAlOutbox(RutaDespachada.Tipo, JsonSerializer.Deserialize<RutaDespachada>(mensaje.Contenido)!, mensaje.OcurridoEn);
            await db.SaveChangesAsync();
            var fila = db.Outbox.Local.Single();
            mensaje = mensaje with { Id = fila.Id };
        }
        var publicador = new PublicadorOutbox(api.Services.GetRequiredService<IServiceScopeFactory>(),
            api.Services.GetRequiredService<IConfiguration>(), api.Services.GetServices<SuscripcionIntegracion>(),
            api.Services.GetRequiredService<ILogger<PublicadorOutbox>>());
        await using var conexion = await new ConnectionFactory { Uri = new(rabbit.ConnectionString) }.CreateConnectionAsync();
        await using (var cerrado = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true)))
        {
            await cerrado.CloseAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => publicador.PublicarPendientesAsync(cerrado, default));
        }
        await using (var scope = api.Services.CreateAsyncScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>()
                .Outbox.SingleAsync(m => m.Id == mensaje.Id)).PublicadoEn);
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true));
        await RabbitMqTransport.DeclararAsync(canal, api.Services.GetServices<SuscripcionIntegracion>(), default);
        await publicador.PublicarPendientesAsync(canal, default);
        using var worker = CrearWorker();
        await worker.StartAsync();
        await EsperarAsync(async () =>
        {
            await using var scope = worker.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
            return await scope.ServiceProvider.GetRequiredService<SeguimientoDbContext>().Inbox.AnyAsync(m => m.MessageId == mensaje.Id)
                ? new object() : null;
        });
        await worker.StopAsync();
        await using var final = api.Services.CreateAsyncScope();
        Assert.NotNull((await final.ServiceProvider.GetRequiredService<PlanificacionDbContext>()
            .Outbox.SingleAsync(m => m.Id == mensaje.Id)).PublicadoEn);
    }

    [Fact]
    public async Task Error_transitorio_reintenta_y_mensaje_invalido_va_a_fallidos()
    {
        using var client = factory.CreateClient();
        using var worker = CrearWorker(fallarUnaVez: true);
        await worker.StartAsync();
        await using var conexion = await new ConnectionFactory { Uri = new(rabbit.ConnectionString) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true));
        await RabbitMqTransport.DeclararAsync(canal, [new(NotificarRutaDespachada.Cola, RutaDespachada.Tipo)], default);
        var mensaje = CrearMensaje(Guid.NewGuid(), Operador, Repartidor);
        await canal.BasicPublishAsync(RabbitMqTransport.Exchange, mensaje.Tipo, true,
            RabbitMqTransport.Propiedades(mensaje), Encoding.UTF8.GetBytes(mensaje.Contenido));
        await EsperarAsync(async () =>
        {
            await using var scope = worker.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
            return await scope.ServiceProvider.GetRequiredService<SeguimientoDbContext>().Inbox.AnyAsync(m => m.MessageId == mensaje.Id)
                ? new object() : null;
        });
        Assert.True(worker.Services.GetRequiredService<FalloTransitorio>().Intentos >= 2);
        var invalido = CrearMensaje(Guid.NewGuid(), Operador, Repartidor) with { Contenido = "{}" };
        await canal.BasicPublishAsync(RabbitMqTransport.Exchange, invalido.Tipo, true,
            RabbitMqTransport.Propiedades(invalido), Encoding.UTF8.GetBytes(invalido.Contenido));
        var fallido = await EsperarAsync(async () => await canal.BasicGetAsync(RabbitMqTransport.Fallidos, autoAck: true));
        Assert.Equal(invalido.Id.ToString(), fallido.BasicProperties.MessageId);
        Assert.Equal(1, fallido.BasicProperties.Headers!["intento"]);
        await worker.StopAsync();
    }

    private IHost CrearWorker(bool fallarUnaVez = false)
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
        if (fallarUnaVez)
        {
            builder.Services.AddSingleton<FalloTransitorio>();
            builder.Services.AddScoped<NotificarRutaDespachada>();
            builder.Services.RemoveAllKeyed<IConsumidorIntegracion>(NotificarRutaDespachada.Cola);
            builder.Services.AddKeyedScoped<IConsumidorIntegracion, ConsumidorQueFallaUnaVez>(NotificarRutaDespachada.Cola);
        }
        builder.Services.AddHostedService<ConsumidorRabbitMq>();
        return builder.Build();
    }

    private sealed class FalloTransitorio { public int Intentos; }
    private sealed class ConsumidorQueFallaUnaVez(FalloTransitorio fallo, NotificarRutaDespachada consumidor) : IConsumidorIntegracion
    {
        public Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
        {
            if (Interlocked.Increment(ref fallo.Intentos) == 1) throw new IOException("Fallo transitorio simulado.");
            return consumidor.ProcesarAsync(mensaje, ct);
        }
    }

    private static async Task ProcesarAsync(IServiceProvider services, MensajeIntegracion mensaje)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
        await scope.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(NotificarRutaDespachada.Cola).ProcesarAsync(mensaje, default);
    }

    private static MensajeIntegracion CrearMensaje(Guid ruta, Guid operador, Guid repartidor)
        => new(Guid.NewGuid(), RutaDespachada.Tipo, operador, null, DateTimeOffset.UtcNow, null,
            JsonSerializer.Serialize(new RutaDespachada(ruta, repartidor, new(2042, 1, 1), 2, null)));

    private static async Task<T> EsperarAsync<T>(Func<Task<T?>> consulta) where T : class
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        do
        {
            if (await consulta() is { } resultado) return resultado;
            await Task.Delay(200);
        } while (DateTime.UtcNow < limite);
        throw new TimeoutException("El aviso no se procesó dentro del plazo de la prueba.");
    }

    private static async Task<(RutaResponse, CrearEnvioResponse)> CrearRutaAsync(HttpClient client)
    {
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana CU43", "099123456", null, null),
            new DireccionRequest("Av. Italia", Guid.NewGuid().ToString("N"), "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        creado.EnsureSuccessStatusCode();
        var envio = (await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        (await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest(envio.Numero + "-1", null))).EnsureSuccessStatusCode();
        var fecha = new DateOnly(2042, 1, 1).AddDays(Interlocked.Increment(ref dia));
        var respuesta = await client.PostAsJsonAsync("/api/planificacion/rutas", new CrearRutaRequest(fecha, Repartidor,
            Guid.Parse("51111111-1111-1111-1111-111111111111"), [envio.Id]));
        respuesta.EnsureSuccessStatusCode();
        return ((await respuesta.Content.ReadFromJsonAsync<RutaResponse>())!, envio);
    }
}
