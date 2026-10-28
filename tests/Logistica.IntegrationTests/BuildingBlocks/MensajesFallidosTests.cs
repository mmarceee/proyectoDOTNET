using System.Net;
using System.Text;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.IntegrationTests.Identidad;
using Logistica.IntegrationTests.Seguimiento;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace Logistica.IntegrationTests.BuildingBlocks;

// CU-09 y ADR-0003, sección 2.6: el registrador vacía logistica.fallidos en mensajes_fallidos, con el inquilino del sobre,
// y el administrador reintenta (mismo MessageId, en su cola de origen) o descarta (con motivo).
public sealed class MensajesFallidosTests(PostgresApiFactory factory, RabbitMqFixture rabbit)
    : IClassFixture<PostgresApiFactory>, IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task El_registrador_guarda_cada_fallido_con_su_operador_y_aparta_los_que_no_tienen()
    {
        using var client = factory.CreateClient(); // Inicializa y migra la API.
        await using var conexion = await ConectarAsync();
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true));
        await RegistradorFallidos.DeclararAsync(canal, default);
        var (operador, otro) = (Guid.NewGuid(), Guid.NewGuid());
        var mensaje = Mensaje(operador);

        await PublicarFallidoAsync(canal, mensaje, "seguimiento.pruebas", intento: 4, error: "IOException");
        await PublicarFallidoAsync(canal, mensaje with { OperadorId = Guid.Empty }, "seguimiento.pruebas", 1, "InvalidDataException");
        var registrador = Registrador();
        while (await registrador.RegistrarSiguienteAsync(canal, default)) { }

        var propio = await Fallidos(operador).ListarAsync(incluirResueltos: false, default);
        var fila = Assert.Single(propio);
        Assert.Equal(("EnvioEntregado.v1", "seguimiento.pruebas", 4, "IOException", "Pendiente"),
            (fila.Tipo, fila.ColaOrigen, fila.Intentos, fila.UltimoError!, fila.Estado));
        Assert.Empty(await Fallidos(otro).ListarAsync(incluirResueltos: true, default));
        Assert.Null(await Fallidos(otro).ObtenerAsync(fila.Id, default));
        // Sin inquilino: no se guarda; queda en su propia cola, visible sólo en la consola de RabbitMQ.
        var aparte = await canal.BasicGetAsync(RabbitMqTransport.FallidosSinInquilino, autoAck: true);
        Assert.NotNull(aparte);
        Assert.Equal(mensaje.Id.ToString(), aparte.BasicProperties.MessageId);
    }

    [Fact]
    public async Task Reintentar_publica_en_la_cola_de_origen_con_el_mismo_MessageId_y_si_vuelve_a_fallar_vuelve_a_la_lista()
    {
        using var client = factory.CreateClient();
        await using var conexion = await ConectarAsync();
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true));
        await RegistradorFallidos.DeclararAsync(canal, default);
        var cola = $"pruebas.cu09.{Guid.NewGuid():N}";
        await canal.QueueDeclareAsync(cola, durable: false, exclusive: false, autoDelete: true);
        var operador = Guid.NewGuid();
        var mensaje = Mensaje(operador);
        await PublicarFallidoAsync(canal, mensaje, cola, intento: 4, error: "IOException");
        await Registrador().RegistrarSiguienteAsync(canal, default);
        var fallidos = Fallidos(operador);
        var id = Assert.Single(await fallidos.ListarAsync(false, default)).Id;

        Assert.Equal(1, await fallidos.ReintentarAsync([id], "admin@operador-demo.uy", default));
        Assert.Equal(0, await Fallidos(operador).ReintentarAsync([id], "admin@operador-demo.uy", default));

        var reintento = await canal.BasicGetAsync(cola, autoAck: true);
        Assert.NotNull(reintento);
        Assert.Equal(mensaje.Id.ToString(), reintento.BasicProperties.MessageId);
        // Sin la cabecera de intentos: recorre de nuevo los reintentos (A3).
        Assert.Equal(0, RabbitMqTransport.Leer(reintento.BasicProperties, reintento.Body).Intento);
        Assert.Equal(mensaje.Contenido, Encoding.UTF8.GetString(reintento.Body.Span));
        var detalle = (await Fallidos(operador).ObtenerAsync(id, default))!;
        Assert.Equal(("Reintentado", "admin@operador-demo.uy"), (detalle.Resumen.Estado, detalle.ResueltoPor!));
        Assert.Empty(await Fallidos(operador).ListarAsync(false, default));

        await PublicarFallidoAsync(canal, mensaje, cola, intento: 4, error: "TimeoutException");
        await Registrador().RegistrarSiguienteAsync(canal, default);
        var otraVez = (await Fallidos(operador).ObtenerAsync(id, default))!;
        Assert.Equal(("Pendiente", "TimeoutException", 2), (otraVez.Resumen.Estado, otraVez.Resumen.UltimoError!, otraVez.Historial.Count));
    }

    [Fact]
    public async Task Descartar_exige_motivo_y_registra_quien_lo_hizo()
    {
        using var client = factory.CreateClient();
        var operador = Guid.NewGuid();
        Guid id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await using var db = Contexto(scope, operador);
            var fallido = MensajeFallido.Registrar(Mensaje(operador), "seguimiento.pruebas", 4, "IOException", DateTimeOffset.UtcNow);
            db.MensajesFallidos.Add(fallido);
            await db.SaveChangesAsync();
            id = fallido.Id;
        }

        await Assert.ThrowsAsync<DomainException>(() => Fallidos(operador).DescartarAsync(id, " ", "admin", default));
        Assert.False(await Fallidos(Guid.NewGuid()).DescartarAsync(id, "Prueba", "admin", default));
        Assert.True(await Fallidos(operador).DescartarAsync(id, "El envío ya se resolvió a mano.", "admin@operador-demo.uy", default));

        var detalle = (await Fallidos(operador).ObtenerAsync(id, default))!;
        Assert.Equal(("Descartado", "admin@operador-demo.uy", "El envío ya se resolvió a mano."),
            (detalle.Resumen.Estado, detalle.ResueltoPor!, detalle.MotivoDescarte!));
        Assert.Empty(await Fallidos(operador).ListarAsync(false, default));
        Assert.Single(await Fallidos(operador).ListarAsync(true, default));
    }

    [Fact]
    public async Task La_pantalla_es_solo_del_administrador_y_muestra_los_fallidos_de_su_operador()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"));
        using var admin = api.CreateClient(Sesion.Opciones);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            await using var db = Contexto(scope, DatosIniciales.OperadorDemoId);
            db.MensajesFallidos.Add(MensajeFallido.Registrar(Mensaje(DatosIniciales.OperadorDemoId) with { Tipo = "PruebaCU09.v1" },
                "seguimiento.pruebas", 4, "IOException", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");
        var listado = await admin.GetAsync("/backoffice/mensajes-fallidos");
        Assert.Equal(HttpStatusCode.OK, listado.StatusCode);
        Assert.Contains("PruebaCU09.v1", await listado.Content.ReadAsStringAsync());

        using var despacho = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await despacho.GetAsync("/backoffice/mensajes-fallidos")).StatusCode);
    }

    private RegistradorFallidos Registrador() => new(factory.Services.GetRequiredService<IServiceScopeFactory>(),
        new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<RegistradorFallidos>.Instance);

    // IMensajesFallidos con el operador indicado, como lo vería su administrador; publica en el broker de la prueba.
    private MensajesFallidos Fallidos(Guid operador)
    {
        var scope = factory.Services.CreateAsyncScope();
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RabbitMQ"] = rabbit.ConnectionString,
        }).Build();
        return new MensajesFallidos(Contexto(scope, operador), configuracion, TimeProvider.System);
    }

    private static MensajeriaDbContext Contexto(AsyncServiceScope scope, Guid operador) =>
        ActivatorUtilities.CreateInstance<MensajeriaDbContext>(scope.ServiceProvider, new InquilinoFijo(operador, null));

    private Task<IConnection> ConectarAsync() => new ConnectionFactory { Uri = new(rabbit.ConnectionString) }.CreateConnectionAsync();

    private static MensajeIntegracion Mensaje(Guid operador) => new(Guid.CreateVersion7(), "EnvioEntregado.v1", operador, null,
        DateTimeOffset.UtcNow, null, $"{{\"EnvioId\":\"{Guid.NewGuid()}\"}}");

    // Como lo deja ConsumidorRabbitMq al agotar los reintentos: el sobre, más la cola de origen, los intentos y el error.
    private static async Task PublicarFallidoAsync(IChannel canal, MensajeIntegracion mensaje, string cola, int intento, string error)
    {
        var propiedades = new BasicProperties
        {
            MessageId = mensaje.Id.ToString(), Type = mensaje.Tipo, ContentType = "application/json", Persistent = true,
            Timestamp = new AmqpTimestamp(mensaje.OcurridoEn.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["operador-id"] = mensaje.OperadorId == Guid.Empty ? null : mensaje.OperadorId.ToString(),
                ["intento"] = intento, ["cola-origen"] = cola, ["ultimo-error"] = error,
            },
        };
        await canal.BasicPublishAsync("", RabbitMqTransport.Fallidos, mandatory: true, propiedades, Encoding.UTF8.GetBytes(mensaje.Contenido));
    }
}
