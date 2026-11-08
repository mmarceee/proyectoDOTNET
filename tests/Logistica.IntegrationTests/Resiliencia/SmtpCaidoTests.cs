using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Seguimiento;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Logistica.IntegrationTests.Resiliencia.Interrupciones;

namespace Logistica.IntegrationTests.Resiliencia;

// Caso 4 de docs/pruebas-de-resiliencia.md: el servidor de correo (Mailpit, en producción Brevo) detenido. El envío se
// crea igual; el Worker intenta mandar el aviso al destinatario (CU-62), registra el intento fallido y la mensajería lo
// reintenta (5 s, 30 s y 2 min, ADR-0003). Cuando el servidor vuelve, el reintento entrega el mismo correo.
[Collection(ContenedoresPropios.Nombre)]
public sealed class SmtpCaidoTests(PostgresApiFactory factory, RabbitMqFixture rabbit, ITestOutputHelper salida)
    : IClassFixture<PostgresApiFactory>, IClassFixture<RabbitMqFixture>, IAsyncLifetime
{
    private ContenedorInterrumpible mailpit = null!;

    public async Task InitializeAsync()
    {
        mailpit = await ContenedorInterrumpible.IniciarAsync("axllent/mailpit:v1.28", [1025, 8025], b => b
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1025).UntilInternalTcpPortIsAvailable(8025)));
    }

    public async Task DisposeAsync() => await mailpit.DisposeAsync();

    [Fact]
    public async Task Sin_servidor_de_correo_el_aviso_queda_en_reintento_y_llega_cuando_vuelve()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:RabbitMQ", rabbit.ConnectionString));
        using var portal = await PortalAsync(api);
        using var worker = CrearWorker(factory.ConnectionString, rabbit.ConnectionString,
            $"smtp://{mailpit.Host}:{mailpit.Puerto(1025)}");
        using var correos = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Host}:{mailpit.Puerto(8025)}/api/v1/") };
        var email = $"resiliencia-{Guid.NewGuid():N}@example.com";

        await mailpit.DetenerAsync();

        // Durante: el envío se crea igual (el aviso es asíncrono) y el Worker registra el intento fallido.
        var respuesta = await CrearEnvioAsync(portal, email);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var envio = (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!.Id;
        await worker.StartAsync();
        var fallida = await EsperarAsync(async () => await NotificacionAsync(api.Services, envio) is { Intentos: > 0 } n ? n : null,
            TimeSpan.FromSeconds(60), "el Worker registra el intento fallido");
        Assert.NotEqual(EstadoNotificacion.Enviada, fallida.Estado);
        Assert.NotNull(fallida.UltimoError);
        salida.WriteLine($"Notificación {fallida.Estado} tras {fallida.Intentos} intento(s): {fallida.UltimoError}");

        await mailpit.ReanudarAsync();
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        // Después: sin reiniciar el Worker, un reintento entrega el correo.
        await EsperarAsync(async () =>
        {
            try
            {
                using var json = JsonDocument.Parse(await correos.GetStringAsync($"search?query=to:{Uri.EscapeDataString(email)}"));
                return json.RootElement.GetProperty("messages").GetArrayLength() > 0 ? "ok" : null;
            }
            catch (HttpRequestException)
            {
                return null; // Mailpit todavía arrancando
            }
        }, TimeSpan.FromSeconds(90), "el correo llega a Mailpit después de que vuelve");
        var enviada = await EsperarAsync(async () => await NotificacionAsync(api.Services, envio) is { Estado: EstadoNotificacion.Enviada } n ? n : null,
            TimeSpan.FromSeconds(15), "la notificación queda Enviada");
        Assert.True(enviada.Intentos >= 2, $"La notificación se envió en {enviada.Intentos} intentos.");
        salida.WriteLine($"Correo entregado {reloj.Elapsed.TotalSeconds:0.0} s después de que volvió el servidor, en el intento {enviada.Intentos}.");
        await worker.StopAsync();
    }

    private static async Task<Notificacion?> NotificacionAsync(IServiceProvider services, Guid envio)
    {
        await using var scope = services.CreateAsyncScope();
        await using var db = ActivatorUtilities.CreateInstance<SeguimientoDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        return await db.Notificaciones.AsNoTracking().SingleOrDefaultAsync(n => n.EnvioId == envio);
    }
}
