using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Contracts.Events;
using Logistica.Modules.Seguimiento;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.EnviarInvitacion;
using Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-02, paso 7: el correo de invitación. Las dos primeras pruebas llaman al consumidor con un emisor falso; la última
// recorre el camino real: alta en el Backoffice → outbox → RabbitMQ → Worker → Mailpit.
[Collection(ContenedoresPropios.Nombre)]
public sealed class EnviarInvitacionTests(PostgresApiFactory factory, RabbitMqFixture rabbit, MailpitFixture mailpit)
    : IClassFixture<PostgresApiFactory>, IClassFixture<RabbitMqFixture>, IClassFixture<MailpitFixture>
{
    private const string UrlPublica = "https://logistica.example";

    [Fact]
    public async Task Manda_la_invitacion_una_sola_vez_con_el_enlace_absoluto()
    {
        var emisor = new EmisorFalso();
        using var api = CrearApi(emisor);
        var mensaje = Invitacion(Guid.NewGuid(), "rita@example.com", reenvio: false);

        await ProcesarAsync(api.Services, mensaje);
        await ProcesarAsync(api.Services, mensaje);

        var correo = Assert.Single(emisor.Enviados);
        Assert.Equal(("rita@example.com", "Rita Invitada"), (correo.Para, correo.NombrePara!));
        Assert.Equal($"{UrlPublica}/cuenta/activar?usuario=1&token=abc", correo.Enlace);
        Assert.Equal(NotificarDestinatarioHandler.MarcaNeutra, correo.Marca);
        Assert.Equal("Definir mi contraseña", correo.Boton);
        Assert.Equal(mensaje.Id, correo.Id);
    }

    // Si el correo falla, no queda registrado como procesado: el reintento de la mensajería lo manda.
    [Fact]
    public async Task Un_fallo_del_correo_se_reintenta()
    {
        var emisor = new EmisorFalso(fallos: 1);
        using var api = CrearApi(emisor);
        var mensaje = Invitacion(Guid.NewGuid(), "rita@example.com", reenvio: true);

        await Assert.ThrowsAsync<InvitacionNoEnviadaException>(() => ProcesarAsync(api.Services, mensaje));
        await ProcesarAsync(api.Services, mensaje);

        Assert.StartsWith("Nuevo enlace para activar tu cuenta", Assert.Single(emisor.Enviados).Asunto);
    }

    [Fact]
    public async Task Un_usuario_creado_en_el_Backoffice_recibe_la_invitacion_en_Mailpit()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("ConnectionStrings:RabbitMQ", rabbit.ConnectionString));
        using var admin = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");
        var email = $"invitado-{Guid.NewGuid():N}@example.com";
        var alta = await Sesion.EnviarFormularioAsync(admin, "/backoffice/usuarios/nuevo",
            new() { ["Nombre"] = "Usuario Invitado", ["Email"] = email, ["Perfil"] = "Despachador" });
        Assert.Equal(HttpStatusCode.Redirect, alta.StatusCode);

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
        var texto = correo.RootElement.GetProperty("Text").GetString();
        Assert.Contains($"{UrlPublica}/cuenta/activar?usuario=", texto);
        Assert.Contains("Usuario Invitado", texto);
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

    private static MensajeIntegracion Invitacion(Guid operador, string email, bool reenvio) =>
        new(Guid.CreateVersion7(), UsuarioInvitado.Tipo, operador, null, DateTimeOffset.UtcNow, null,
            JsonSerializer.Serialize(new UsuarioInvitado(Guid.NewGuid(), email, "Rita Invitada", "Repartidor",
                "/cuenta/activar?usuario=1&token=abc", reenvio)));

    private static async Task ProcesarAsync(IServiceProvider services, MensajeIntegracion mensaje)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
        await scope.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(EnviarInvitacion.Cola)
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
        throw new TimeoutException("La invitación no llegó a Mailpit dentro del plazo de la prueba.");
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
