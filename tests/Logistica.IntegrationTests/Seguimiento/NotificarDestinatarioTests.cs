using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Contracts.Events;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Logistica.IntegrationTests.Seguimiento;

public sealed class MailpitFixture : IAsyncLifetime
{
    private readonly IContainer servidor = new ContainerBuilder("axllent/mailpit:v1.28")
        .WithPortBinding(1025, true).WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1025).UntilInternalTcpPortIsAvailable(8025))
        .Build();
    public string Smtp => $"smtp://{servidor.Hostname}:{servidor.GetMappedPublicPort(1025)}";
    public Uri Api => new($"http://{servidor.Hostname}:{servidor.GetMappedPublicPort(8025)}/api/v1/");
    public Task InitializeAsync() => servidor.StartAsync();
    public Task DisposeAsync() => servidor.DisposeAsync().AsTask();
}

// CU-62: notificaciones al destinatario. Cada prueba usa su propio operador y comercio, así no se mezclan sus datos.
public sealed class NotificarDestinatarioTests(PostgresApiFactory factory, RabbitMqFixture rabbit, MailpitFixture mailpit)
    : IClassFixture<PostgresApiFactory>, IClassFixture<RabbitMqFixture>, IClassFixture<MailpitFixture>
{
    private const string UrlPublica = "https://logistica.example";
    private static readonly DateTimeOffset Ahora = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Avisa_cada_hecho_relevante_una_sola_vez_con_el_enlace_de_seguimiento()
    {
        var emisor = new EmisorFalso();
        using var api = CrearApi(emisor);
        var (operador, comercio, envio) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var alta = Alta(operador, comercio, envio, "ana@example.com");

        await ProcesarAsync(api.Services, alta);
        await ProcesarAsync(api.Services, alta);
        // En tránsito lo publica el repartidor: el sobre no trae comercio.
        var transito = Mensaje(EnvioEnTransito.Tipo, new EnvioEnTransito(envio, "ENV-CU62-A", Ahora, null), operador, null);
        await ProcesarAsync(api.Services, transito);

        Assert.Equal(2, emisor.Enviados.Count);
        var primero = emisor.Enviados.First();
        Assert.Equal(("ana@example.com", "Ana CU62"), (primero.Para, primero.NombrePara!));
        Assert.Equal("Recibimos tu envío ENV-CU62-A", primero.Asunto);
        Assert.Equal(NotificarDestinatarioHandler.MarcaNeutra, primero.Marca);
        // El enlace es absoluto y su token abre el seguimiento de este envío y este operador.
        Assert.StartsWith($"{UrlPublica}/seguimiento/", primero.Enlace);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var token = primero.Enlace[$"{UrlPublica}/seguimiento/".Length..];
            Assert.True(scope.ServiceProvider.GetRequiredService<TokenSeguimiento>().TryLeer(token, out var op, out var id));
            Assert.Equal((operador, envio), (op, id));
        }
        Assert.Equal("Tu envío ENV-CU62-A está en camino", emisor.Enviados.Last().Asunto);

        await using var lectura = api.Services.CreateAsyncScope();
        await using var db = Contexto(lectura, operador);
        var notificaciones = await db.Notificaciones.Where(n => n.EnvioId == envio).ToListAsync();
        Assert.Equal(2, notificaciones.Count);
        Assert.All(notificaciones, n => Assert.Equal((EstadoNotificacion.Enviada, 1, comercio), (n.Estado, n.Intentos, n.ComercioId)));
    }

    [Fact]
    public async Task Sin_correo_del_destinatario_queda_no_enviable_sin_reintentos()
    {
        var emisor = new EmisorFalso();
        using var api = CrearApi(emisor);
        var (operador, comercio, envio) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await ProcesarAsync(api.Services, Alta(operador, comercio, envio, null));

        Assert.Empty(emisor.Enviados);
        await using var scope = api.Services.CreateAsyncScope();
        await using var db = Contexto(scope, operador);
        var notificacion = await db.Notificaciones.SingleAsync(n => n.EnvioId == envio);
        Assert.Equal((EstadoNotificacion.NoEnviable, 0), (notificacion.Estado, notificacion.Intentos));
    }

    [Fact]
    public async Task Un_fallo_se_reintenta_el_ultimo_la_deja_fallida_y_CU09_la_recupera()
    {
        var emisor = new EmisorFalso(fallos: 2);
        using var api = CrearApi(emisor);
        var (operador, comercio, envio) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var alta = Alta(operador, comercio, envio, "ana@example.com");

        await Assert.ThrowsAsync<NotificacionNoEnviadaException>(() => ProcesarAsync(api.Services, alta));
        Assert.Equal(EstadoNotificacion.Pendiente, (await LeerAsync(api.Services, operador, envio)).Estado);

        await Assert.ThrowsAsync<NotificacionNoEnviadaException>(() =>
            ProcesarAsync(api.Services, alta with { Intento = RabbitMqTransport.Esperas.Length }));
        var fallida = await LeerAsync(api.Services, operador, envio);
        Assert.Equal((EstadoNotificacion.Fallida, 2), (fallida.Estado, fallida.Intentos));
        Assert.NotNull(fallida.UltimoError);

        // CU-09 vuelve a publicar el mismo mensaje.
        await ProcesarAsync(api.Services, alta);
        Assert.Equal(EstadoNotificacion.Enviada, (await LeerAsync(api.Services, operador, envio)).Estado);
        Assert.Single(emisor.Enviados);
    }

    [Fact]
    public async Task Un_hecho_sin_alta_se_reintenta_hasta_que_llegue()
    {
        using var api = CrearApi(new EmisorFalso());
        var (operador, envio) = (Guid.NewGuid(), Guid.NewGuid());
        var entregado = Mensaje(EnvioEntregado.Tipo, new EnvioEntregado(envio, "ENV-CU62-D", Ahora, null), operador, null);

        await Assert.ThrowsAsync<EnvioNoRegistradoException>(() => ProcesarAsync(api.Services, entregado));
    }

    [Fact]
    public async Task El_correo_usa_la_ultima_identidad_visual_que_guardo_el_operador()
    {
        var emisor = new EmisorFalso();
        using var api = CrearApi(emisor);
        var (operador, comercio, envio) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var nueva = Mensaje(IdentidadVisualActualizada.Tipo,
            new IdentidadVisualActualizada("Operador Norte", "#0A7A3C", "#FFFFFF", "hola@norte.uy", "2900 0000"), operador, null);
        var vieja = Mensaje(IdentidadVisualActualizada.Tipo,
            new IdentidadVisualActualizada("Nombre anterior", "#000000", "#FFFFFF", null, null), operador, null) with
        {
            OcurridoEn = Ahora.AddDays(-1),
        };

        await ProcesarAsync(api.Services, nueva, ActualizarIdentidadOperador.Cola);
        await ProcesarAsync(api.Services, vieja, ActualizarIdentidadOperador.Cola);
        await ProcesarAsync(api.Services, Alta(operador, comercio, envio, "ana@example.com"));

        Assert.Equal(new MarcaCorreo("Operador Norte", "#0A7A3C", "hola@norte.uy", "2900 0000"), emisor.Enviados.Single().Marca);
    }

    [Fact]
    public async Task Un_envio_creado_en_el_Portal_llega_por_correo_a_Mailpit_a_traves_de_RabbitMQ_y_el_Worker()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("ConnectionStrings:RabbitMQ", rabbit.ConnectionString));
        using var portal = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(portal, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(portal, DatosIniciales.OperadorDemoId);
        var email = $"destinatario-{Guid.NewGuid():N}@example.com";
        var respuesta = await portal.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana CU62", "099123456", email, null),
            new DireccionRequest("Av. Italia", Guid.NewGuid().ToString("N"), "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        respuesta.EnsureSuccessStatusCode();
        var envio = (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;

        using var worker = CrearWorker();
        await worker.StartAsync();
        using var http = new HttpClient { BaseAddress = mailpit.Api };
        var id = await EsperarAsync(async () =>
        {
            using var json = JsonDocument.Parse(await http.GetStringAsync($"search?query=to:{Uri.EscapeDataString(email)}"));
            return json.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("ID").GetString())
                .FirstOrDefault();
        });
        await worker.StopAsync();

        using var correo = JsonDocument.Parse(await http.GetStringAsync($"message/{id}"));
        var raiz = correo.RootElement;
        Assert.Equal($"Recibimos tu envío {envio.Numero}", raiz.GetProperty("Subject").GetString());
        Assert.Contains($"{UrlPublica}/seguimiento/", raiz.GetProperty("Text").GetString());
        Assert.Contains("Ana CU62", raiz.GetProperty("Text").GetString());
        Assert.Equal("avisos@logistica.local", raiz.GetProperty("From").GetProperty("Address").GetString());
    }

    private WebApplicationFactory<Program> CrearApi(EmisorFalso emisor) =>
        factory.WithWebHostBuilder(b => b.UseSetting("Aplicacion:UrlPublica", UrlPublica)
            .ConfigureServices(s => s.AddScoped<IEmisorCorreo>(_ => emisor)));

    private IHost CrearWorker()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = factory.ConnectionString,
            ["ConnectionStrings:RabbitMQ"] = rabbit.ConnectionString,
            ["ConnectionStrings:Smtp"] = mailpit.Smtp,
            ["Seguimiento:Correo:Remitente"] = "avisos@logistica.local",
            ["Seguimiento:ClaveToken"] = Convert.ToBase64String(new byte[32]),
            ["Aplicacion:UrlPublica"] = UrlPublica,
        });
        builder.Services.AddScoped<InquilinoMensaje>();
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<InquilinoMensaje>());
        builder.Services.AddSeguimientoModule(builder.Configuration);
        builder.Services.AddHostedService<ConsumidorRabbitMq>();
        return builder.Build();
    }

    private static SeguimientoDbContext Contexto(AsyncServiceScope scope, Guid operador) =>
        ActivatorUtilities.CreateInstance<SeguimientoDbContext>(scope.ServiceProvider, new InquilinoFijo(operador, null));

    private static async Task<Notificacion> LeerAsync(IServiceProvider services, Guid operador, Guid envio)
    {
        await using var scope = services.CreateAsyncScope();
        await using var db = Contexto(scope, operador);
        return await db.Notificaciones.AsNoTracking().SingleAsync(n => n.EnvioId == envio);
    }

    private static MensajeIntegracion Alta(Guid operador, Guid comercio, Guid envio, string? email) =>
        Mensaje(EnvioAdmitido.Tipo, new EnvioAdmitido(envio, "ENV-CU62-A", Ahora, "Ana CU62", email), operador, comercio);

    private static MensajeIntegracion Mensaje<T>(string tipo, T contenido, Guid operador, Guid? comercio) =>
        new(Guid.CreateVersion7(), tipo, operador, comercio, Ahora, null, JsonSerializer.Serialize(contenido));

    private static async Task ProcesarAsync(IServiceProvider services, MensajeIntegracion mensaje, string? cola = null)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
        await scope.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(cola ?? NotificarDestinatario.Cola(mensaje.Tipo))
            .ProcesarAsync(mensaje, default);
    }

    private static async Task<T> EsperarAsync<T>(Func<Task<T?>> consulta) where T : class
    {
        var limite = DateTime.UtcNow.AddSeconds(45);
        do
        {
            if (await consulta() is { } resultado) return resultado;
            await Task.Delay(200);
        } while (DateTime.UtcNow < limite);
        throw new TimeoutException("El correo no llegó a Mailpit dentro del plazo de la prueba.");
    }

    // Falla las primeras veces que se le pide, y después acepta.
    private sealed class EmisorFalso(int fallos = 0) : IEmisorCorreo
    {
        private int _pedidos;
        public ConcurrentQueue<MensajeCorreo> Enviados { get; } = new();

        public Task<ResultadoEnvioCorreo> EnviarAsync(MensajeCorreo mensaje, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _pedidos) <= fallos)
                return Task.FromResult(new ResultadoEnvioCorreo(false, "Servidor de correo caído (simulado)."));
            Enviados.Enqueue(mensaje);
            return Task.FromResult(new ResultadoEnvioCorreo(true, null));
        }
    }
}
