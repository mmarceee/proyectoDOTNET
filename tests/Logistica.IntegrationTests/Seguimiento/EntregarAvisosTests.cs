using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Avisos;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.EntregarAvisos;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Http;
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

// CU-64: preparación y entrega de los avisos a los comercios. Cada prueba usa su propio operador y comercio, así las
// suscripciones de una no reciben los hechos de otra aunque compartan la base.
public sealed class EntregarAvisosTests(PostgresApiFactory factory, RabbitMqFixture rabbit)
    : IClassFixture<PostgresApiFactory>, IClassFixture<RabbitMqFixture>
{
    private static readonly string Clave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly DateTimeOffset Ahora = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Preparar_crea_una_entrega_por_suscripcion_que_recibe_el_hecho_sin_duplicar()
    {
        using var api = CrearApi(new Receptor());
        var (operador, comercio, envio) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var todos = await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.CambioDeEstado);
        var fallidas = await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.EntregaFallida);
        var devoluciones = await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.Devolucion);
        var inactiva = await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.CambioDeEstado);
        await DesactivarAsync(api.Services, inactiva.Suscripcion);
        var otroComercio = await SuscribirAsync(api.Services, operador, Guid.NewGuid(), TipoEventoAviso.CambioDeEstado);

        var alta = Mensaje(EnvioAdmitido.Tipo, new EnvioAdmitido(envio, "ENV-CU64-A", Ahora), operador, comercio);
        await ProcesarAsync(api.Services, PrepararAvisos.Cola(EnvioAdmitido.Tipo), alta);
        await ProcesarAsync(api.Services, PrepararAvisos.Cola(EnvioAdmitido.Tipo), alta);
        // T7 y T9 desde la sesión del repartidor: el sobre no trae comercio.
        var fallo = Mensaje(IntentoFallidoRegistrado.Tipo,
            new IntentoFallidoRegistrado(envio, "ENV-CU64-A", 1, "Ausente", "Reprogramado", Ahora, null), operador, null);
        await ProcesarAsync(api.Services, PrepararAvisos.Cola(IntentoFallidoRegistrado.Tipo), fallo);

        await using var scope = api.Services.CreateAsyncScope();
        await using var db = Contexto(scope, operador, null);
        var seguido = await db.EnviosSeguidos.SingleAsync(e => e.Id == envio);
        Assert.Equal((comercio, "ENV-CU64-A"), (seguido.ComercioId, seguido.Numero));

        var deAlta = await db.EntregasAviso.Where(e => e.EventoId == alta.Id).ToListAsync();
        var entrega = Assert.Single(deAlta);
        Assert.Equal(todos.Suscripcion.Id, entrega.SuscripcionAvisoId);
        Assert.Equal((TipoEventoAviso.CambioDeEstado, "Admitido", "ENV-CU64-A", EstadoEntregaAviso.Pendiente),
            (entrega.Tipo, entrega.EstadoEnvio, entrega.NumeroEnvio, entrega.Estado));

        var deFallo = await db.EntregasAviso.Where(e => e.EventoId == fallo.Id).ToListAsync();
        Assert.Equal(new[] { todos.Suscripcion.Id, fallidas.Suscripcion.Id }.Order(), deFallo.Select(e => e.SuscripcionAvisoId).Order());
        Assert.All(deFallo, e => Assert.Equal((TipoEventoAviso.EntregaFallida, "Reprogramado"), (e.Tipo, e.EstadoEnvio)));
        Assert.DoesNotContain(deAlta.Concat(deFallo), e => e.SuscripcionAvisoId == devoluciones.Suscripcion.Id
            || e.SuscripcionAvisoId == inactiva.Suscripcion.Id || e.SuscripcionAvisoId == otroComercio.Suscripcion.Id);

        // Un AvisoPendiente en el outbox de Seguimiento por cada entrega, en la misma transacción.
        var pendientes = (await db.Outbox.AsNoTracking().Where(m => m.Tipo == AvisoPendiente.Tipo).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<AvisoPendiente>(m.Contenido)!.EntregaId).ToHashSet();
        Assert.All(deAlta.Concat(deFallo), e => Assert.Contains(e.Id, pendientes));
        Assert.True(await db.Inbox.AnyAsync(m => m.MessageId == alta.Id));
    }

    [Fact]
    public async Task Un_hecho_sin_alta_se_reintenta_y_otro_operador_no_ve_el_envio()
    {
        using var api = CrearApi(new Receptor());
        var (operador, comercio, envio) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.CambioDeEstado);
        var entregado = new EnvioEntregado(envio, "ENV-CU64-B", Ahora, null);
        var cola = PrepararAvisos.Cola(EnvioEntregado.Tipo);

        // Sin orden garantizado entre colas: el alta puede llegar después y el reintento la espera.
        var antes = Mensaje(EnvioEntregado.Tipo, entregado, operador, null);
        await Assert.ThrowsAsync<EnvioNoRegistradoException>(() => ProcesarAsync(api.Services, cola, antes));
        await ProcesarAsync(api.Services, PrepararAvisos.Cola(EnvioAdmitido.Tipo),
            Mensaje(EnvioAdmitido.Tipo, new EnvioAdmitido(envio, "ENV-CU64-B", Ahora), operador, comercio));
        await ProcesarAsync(api.Services, cola, antes);
        await Assert.ThrowsAsync<EnvioNoRegistradoException>(() =>
            ProcesarAsync(api.Services, cola, Mensaje(EnvioEntregado.Tipo, entregado, Guid.NewGuid(), null)));

        // Un alta sin comercio en el sobre es inválida: va a la cola de fallidos sin reintentos.
        await Assert.ThrowsAsync<InvalidDataException>(() => ProcesarAsync(api.Services, PrepararAvisos.Cola(EnvioAdmitido.Tipo),
            Mensaje(EnvioAdmitido.Tipo, new EnvioAdmitido(Guid.NewGuid(), "ENV-CU64-C", Ahora), operador, null)));

        await using var scope = api.Services.CreateAsyncScope();
        await using var db = Contexto(scope, operador, null);
        Assert.Single(await db.EntregasAviso.Where(e => e.EventoId == antes.Id).ToListAsync());
        Assert.Single(await db.Inbox.Where(m => m.MessageId == antes.Id).ToListAsync());
    }

    [Fact]
    public async Task La_recepcion_en_deposito_se_avisa_como_cambio_de_estado()
    {
        using var api = CrearApi(new Receptor());
        var (operador, comercio, envio) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var todos = await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.CambioDeEstado);
        await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.EntregaFallida);
        await ProcesarAsync(api.Services, PrepararAvisos.Cola(EnvioAdmitido.Tipo),
            Mensaje(EnvioAdmitido.Tipo, new EnvioAdmitido(envio, "ENV-CU64-T2", Ahora), operador, comercio));

        // T2 lo publica el operario de depósito: el sobre no trae comercio.
        var recibido = Mensaje(EnvioRecibidoEnDeposito.Tipo,
            new EnvioRecibidoEnDeposito(envio, "ENV-CU64-T2", Ahora, null), operador, null);
        await ProcesarAsync(api.Services, PrepararAvisos.Cola(EnvioRecibidoEnDeposito.Tipo), recibido);

        await using var scope = api.Services.CreateAsyncScope();
        await using var db = Contexto(scope, operador, null);
        var entrega = Assert.Single(await db.EntregasAviso.Where(e => e.EventoId == recibido.Id).ToListAsync());
        Assert.Equal((todos.Suscripcion.Id, TipoEventoAviso.CambioDeEstado, "EnDeposito"),
            (entrega.SuscripcionAvisoId, entrega.Tipo, entrega.EstadoEnvio));
    }

    [Fact]
    public async Task Un_fallo_queda_registrado_y_el_reintento_entrega_el_mismo_aviso_firmado()
    {
        var receptor = new Receptor(500, 200);
        using var api = CrearApi(receptor);
        var (operador, comercio) = (Guid.NewGuid(), Guid.NewGuid());
        var suscripcion = await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.CambioDeEstado);
        var entrega = await PrepararAltaAsync(api.Services, operador, comercio, "ENV-CU64-D");
        var aviso = Mensaje(AvisoPendiente.Tipo, new AvisoPendiente(entrega), operador, null);

        await Assert.ThrowsAsync<AvisoNoEntregadoException>(() => ProcesarAsync(api.Services, EntregarAviso.Cola, aviso));
        var fallida = await LeerEntregaAsync(api.Services, operador, entrega);
        Assert.Equal(EstadoEntregaAviso.Fallida, fallida.Estado);
        Assert.Equal(500, Assert.Single(fallida.Intentos).CodigoRespuesta);

        await ProcesarAsync(api.Services, EntregarAviso.Cola, aviso with { Intento = 1 });
        await ProcesarAsync(api.Services, EntregarAviso.Cola, aviso with { Intento = 1 });

        var exitosa = await LeerEntregaAsync(api.Services, operador, entrega);
        Assert.Equal(EstadoEntregaAviso.Exitosa, exitosa.Estado);
        Assert.Equal([500, 200], exitosa.Intentos.OrderBy(i => i.Numero).Select(i => i.CodigoRespuesta));
        // La inbox evita el tercer envío; los dos que llegaron son el mismo aviso, firmados con el secreto vigente.
        Assert.Equal(2, receptor.Recibidos.Count);
        Assert.All(receptor.Recibidos, r =>
        {
            r.Verificar(suscripcion.Secreto);
            Assert.Equal(entrega, r.Id);
            Assert.False(r.Json.GetProperty("prueba").GetBoolean());
            Assert.Equal("ENV-CU64-D", r.Json.GetProperty("numeroEnvio").GetString());
            Assert.Equal("Admitido", r.Json.GetProperty("estado").GetString());
            Assert.Equal("CambioDeEstado", r.Json.GetProperty("tipo").GetString());
        });
    }

    [Fact]
    public async Task El_ultimo_fallo_va_a_la_cola_de_fallidos_y_un_reenvio_lo_recupera()
    {
        var receptor = new Receptor(503, 200);
        using var api = CrearApi(receptor);
        var (operador, comercio) = (Guid.NewGuid(), Guid.NewGuid());
        await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.CambioDeEstado);
        var entrega = await PrepararAltaAsync(api.Services, operador, comercio, "ENV-CU64-E");

        var ultimo = Mensaje(AvisoPendiente.Tipo, new AvisoPendiente(entrega), operador, null) with
        {
            Intento = RabbitMqTransport.Esperas.Length,
        };
        await Assert.ThrowsAsync<AvisoNoEntregadoException>(() => ProcesarAsync(api.Services, EntregarAviso.Cola, ultimo));
        Assert.Equal(EstadoEntregaAviso.EnColaDeFallidos, (await LeerEntregaAsync(api.Services, operador, entrega)).Estado);

        // CU-65 encola otro AvisoPendiente para la misma entrega: el receptor recibe el mismo identificador.
        await ProcesarAsync(api.Services, EntregarAviso.Cola, Mensaje(AvisoPendiente.Tipo, new AvisoPendiente(entrega), operador, null));
        var recuperada = await LeerEntregaAsync(api.Services, operador, entrega);
        Assert.Equal(EstadoEntregaAviso.Exitosa, recuperada.Estado);
        Assert.Equal(2, recuperada.Intentos.Count);
        Assert.All(receptor.Recibidos, r => Assert.Equal(entrega, r.Id));
    }

    [Fact]
    public async Task Una_suscripcion_desactivada_despues_de_preparar_no_recibe_el_aviso()
    {
        var receptor = new Receptor();
        using var api = CrearApi(receptor);
        var (operador, comercio) = (Guid.NewGuid(), Guid.NewGuid());
        var suscripcion = await SuscribirAsync(api.Services, operador, comercio, TipoEventoAviso.CambioDeEstado);
        var entrega = await PrepararAltaAsync(api.Services, operador, comercio, "ENV-CU64-F");
        await DesactivarAsync(api.Services, suscripcion.Suscripcion);

        await ProcesarAsync(api.Services, EntregarAviso.Cola, Mensaje(AvisoPendiente.Tipo, new AvisoPendiente(entrega), operador, null));

        Assert.Empty(receptor.Recibidos);
        var sinEnviar = await LeerEntregaAsync(api.Services, operador, entrega);
        Assert.Equal(EstadoEntregaAviso.Pendiente, sinEnviar.Estado);
        Assert.Empty(sinEnviar.Intentos);
    }

    [Fact]
    public async Task Un_envio_creado_en_el_Portal_llega_firmado_al_receptor_por_RabbitMQ_y_el_Worker()
    {
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("Seguimiento:Avisos:ClaveCifrado", Clave)
            .UseSetting("ConnectionStrings:RabbitMQ", rabbit.ConnectionString));
        using var portal = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(portal, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(portal, DatosIniciales.OperadorDemoId);
        var url = $"https://comercio.example/avisos/{Guid.NewGuid():N}";
        var token = (await portal.GetFromJsonAsync<TokenAntifalsificacionResponse>("/api/avisos/suscripciones/antifalsificacion"))!.Token;
        using var alta = new HttpRequestMessage(HttpMethod.Post, "/api/avisos/suscripciones")
        {
            Content = JsonContent.Create(new CrearSuscripcionAvisoRequest(url, ["CambioDeEstado"])),
        };
        alta.Headers.Add("RequestVerificationToken", token);
        using var creada = await portal.SendAsync(alta);
        creada.EnsureSuccessStatusCode();
        var secreto = (await creada.Content.ReadFromJsonAsync<SecretoSuscripcionAvisoResponse>())!.SecretoFirma;

        var respuesta = await portal.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana CU64", "099123456", null, null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        respuesta.EnsureSuccessStatusCode();
        var envio = (await respuesta.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;

        var receptor = new Receptor();
        using var worker = CrearWorker(receptor);
        await worker.StartAsync();
        var recibido = await EsperarAsync(() => Task.FromResult(receptor.Recibidos.FirstOrDefault(r =>
            r.Url == url && r.Json.GetProperty("numeroEnvio").GetString() == envio.Numero)));
        await worker.StopAsync();

        recibido.Verificar(secreto);
        Assert.Equal("Admitido", recibido.Json.GetProperty("estado").GetString());
        Assert.False(recibido.Json.GetProperty("prueba").GetBoolean());
    }

    private WebApplicationFactory<Program> CrearApi(Receptor receptor) =>
        factory.WithWebHostBuilder(b => b.UseSetting("Seguimiento:Avisos:ClaveCifrado", Clave)
            .ConfigureServices(s => s.AddHttpClient<IEmisorAvisos, EmisorAvisosHttp>()
                .ConfigurePrimaryHttpMessageHandler(() => receptor)));

    private IHost CrearWorker(Receptor receptor)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = factory.ConnectionString,
            ["ConnectionStrings:RabbitMQ"] = rabbit.ConnectionString,
            ["Seguimiento:Avisos:ClaveCifrado"] = Clave,
        });
        builder.Services.AddScoped<InquilinoMensaje>();
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<InquilinoMensaje>());
        builder.Services.AddSeguimientoModule(builder.Configuration);
        builder.Services.AddHttpClient<IEmisorAvisos, EmisorAvisosHttp>().ConfigurePrimaryHttpMessageHandler(() => receptor);
        builder.Services.AddHostedService<ConsumidorRabbitMq>();
        return builder.Build();
    }

    private static SeguimientoDbContext Contexto(AsyncServiceScope scope, Guid operador, Guid? comercio) =>
        ActivatorUtilities.CreateInstance<SeguimientoDbContext>(scope.ServiceProvider, new InquilinoFijo(operador, comercio));

    private sealed record Suscrita(SuscripcionAviso Suscripcion, string Secreto);

    private static async Task<Suscrita> SuscribirAsync(IServiceProvider services, Guid operador, Guid comercio,
        params TipoEventoAviso[] tipos)
    {
        await using var scope = services.CreateAsyncScope();
        var secretos = scope.ServiceProvider.GetRequiredService<ISecretosAviso>();
        var secreto = secretos.Generar();
        var suscripcion = SuscripcionAviso.Crear(operador, comercio, Guid.NewGuid(), "https://comercio.example/avisos",
            tipos, secretos.Cifrar(secreto, operador, comercio));
        await using var db = Contexto(scope, operador, comercio);
        db.SuscripcionesAviso.Add(suscripcion);
        await db.SaveChangesAsync();
        return new(suscripcion, secreto);
    }

    private static async Task DesactivarAsync(IServiceProvider services, SuscripcionAviso suscripcion)
    {
        await using var scope = services.CreateAsyncScope();
        await using var db = Contexto(scope, suscripcion.OperadorId, suscripcion.ComercioId);
        (await db.SuscripcionesAviso.SingleAsync(s => s.Id == suscripcion.Id)).Desactivar();
        await db.SaveChangesAsync();
    }

    // Procesa el alta de un envío nuevo y devuelve la única entrega que preparó.
    private static async Task<Guid> PrepararAltaAsync(IServiceProvider services, Guid operador, Guid comercio, string numero)
    {
        var alta = Mensaje(EnvioAdmitido.Tipo, new EnvioAdmitido(Guid.NewGuid(), numero, Ahora), operador, comercio);
        await ProcesarAsync(services, PrepararAvisos.Cola(EnvioAdmitido.Tipo), alta);
        await using var scope = services.CreateAsyncScope();
        await using var db = Contexto(scope, operador, null);
        return (await db.EntregasAviso.SingleAsync(e => e.EventoId == alta.Id)).Id;
    }

    private static async Task<EntregaAviso> LeerEntregaAsync(IServiceProvider services, Guid operador, Guid entrega)
    {
        await using var scope = services.CreateAsyncScope();
        await using var db = Contexto(scope, operador, null);
        return await db.EntregasAviso.AsNoTracking().Include(e => e.Intentos).SingleAsync(e => e.Id == entrega);
    }

    private static MensajeIntegracion Mensaje<T>(string tipo, T contenido, Guid operador, Guid? comercio) =>
        new(Guid.CreateVersion7(), tipo, operador, comercio, Ahora, null, JsonSerializer.Serialize(contenido));

    private static async Task ProcesarAsync(IServiceProvider services, string cola, MensajeIntegracion mensaje)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
        await scope.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(cola).ProcesarAsync(mensaje, default);
    }

    private static async Task<T> EsperarAsync<T>(Func<Task<T?>> consulta) where T : class
    {
        var limite = DateTime.UtcNow.AddSeconds(45);
        do
        {
            if (await consulta() is { } resultado) return resultado;
            await Task.Delay(200);
        } while (DateTime.UtcNow < limite);
        throw new TimeoutException("El aviso no llegó al receptor dentro del plazo de la prueba.");
    }

    // Receptor en memoria: responde los códigos indicados, en orden, y después 200.
    private sealed class Receptor(params int[] codigos) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<int> _codigos = new(codigos);
        public ConcurrentQueue<Recibido> Recibidos { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cuerpo = await request.Content!.ReadAsByteArrayAsync(ct);
            Recibidos.Enqueue(new(request.RequestUri!.AbsoluteUri, cuerpo,
                request.Headers.GetValues("X-Aviso-Fecha").Single(), request.Headers.GetValues("X-Aviso-Firma").Single()));
            return new HttpResponseMessage(_codigos.TryDequeue(out var codigo) ? (HttpStatusCode)codigo : HttpStatusCode.OK);
        }
    }

    private sealed record Recibido(string Url, byte[] Cuerpo, string Fecha, string Firma)
    {
        public JsonElement Json => JsonDocument.Parse(Cuerpo).RootElement;
        public Guid Id => Json.GetProperty("id").GetGuid();

        // Verificación independiente del firmador del emisor (guía de integración de CU-63).
        public void Verificar(string secreto)
        {
            var esperada = HMACSHA256.HashData(Convert.FromBase64String(secreto),
                Encoding.UTF8.GetBytes(Fecha + ".").Concat(Cuerpo).ToArray());
            Assert.StartsWith("v1=", Firma);
            Assert.True(CryptographicOperations.FixedTimeEquals(esperada, Convert.FromHexString(Firma[3..])));
        }
    }
}
