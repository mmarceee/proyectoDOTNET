using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-61 de punta a punta: la destinataria reprograma desde su enlace, sin usuario (el token fija el inquilino), y lo
// confirma con un código al correo o, si el envío no tiene correo, con los últimos 4 dígitos del teléfono
// (requerimiento especial 4). Las reglas de Demo piden 24 horas de anticipación; la franja de la tarde (14 a 18) rige
// todos los días.
public class ReprogramacionPublicaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid FranjaTarde = Guid.Parse("72222222-2222-2222-2222-222222222222");
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly DateOnly EnTresDias = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
    private static int _dia;
    // Los envíos de prueba no tienen correo: se confirma con los últimos 4 dígitos de 099123456.
    private const string DigitosTelefono = "3456";
    private readonly EmisorFalso _correos = new();

    // El público: sin sesión y sin el respaldo provisorio, como en CU-60. Los correos quedan en el emisor falso.
    private HttpClient Publico() => factory
        .WithWebHostBuilder(b => b
            .UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("Seguimiento:ConsultasPorMinuto", "1000")
            .UseSetting("Aplicacion:UrlPublica", "http://seguimiento.test")
            .ConfigureServices(s => s.AddScoped<IEmisorCorreo>(_ => _correos)))
        .CreateClient();

    // La preparación (tienda, depósito y rutas) con el inquilino provisorio de desarrollo, como las pruebas de CU-41.
    private HttpClient Preparacion() => factory
        .WithWebHostBuilder(b => b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true"))
        .CreateClient();

    // T18: desde el depósito queda Reprogramado, con la fecha y franja elegidas, el evento con origen en el seguimiento
    // público y el aviso en el Outbox. El seguimiento muestra el estado nuevo al instante (caché invalidada).
    [Fact]
    public async Task Desde_el_deposito_queda_reprogramado_y_el_seguimiento_lo_muestra()
    {
        using var preparacion = Preparacion();
        var envio = await EnvioAsync(preparacion, recibido: true);
        using var publico = Publico();
        var token = Token(envio.Id);
        Assert.Equal("En el depósito", (await publico.GetFromJsonAsync<SeguimientoPublicoResponse>($"/api/seguimiento/{token}"))!.Estado);

        var respuesta = await ReprogramarAsync(publico, token, EnTresDias, FranjaTarde);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        var guardado = await LeerAsync(envio.Id);
        Assert.Equal(EstadoEnvio.Reprogramado, guardado.Estado);
        Assert.Equal(EnTresDias, guardado.FechaEntregaProgramada);
        Assert.Equal(FranjaTarde, guardado.FranjaHorariaId);
        Assert.Equal(OrigenEvento.SeguimientoPublico, guardado.Eventos.OrderBy(e => e.OcurridoEn).Last().Origen);
        Assert.Contains(await MensajesAsync(), m => m.Contains(envio.Id.ToString()) && m.Contains("SeguimientoPublico"));

        var despues = await publico.GetFromJsonAsync<SeguimientoPublicoResponse>($"/api/seguimiento/{token}");
        Assert.NotEqual("En el depósito", despues!.Estado);
    }

    // Admitido: se registra la nueva fecha y franja, sin cambio de estado.
    [Fact]
    public async Task Admitido_guarda_la_nueva_entrega_sin_cambiar_de_estado()
    {
        using var preparacion = Preparacion();
        var envio = await EnvioAsync(preparacion, recibido: false);
        using var publico = Publico();

        var respuesta = await ReprogramarAsync(publico, Token(envio.Id), EnTresDias, FranjaTarde);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        var guardado = await LeerAsync(envio.Id);
        Assert.Equal(EstadoEnvio.Admitido, guardado.Estado);
        Assert.Equal(EnTresDias, guardado.FechaEntregaProgramada);
    }

    // T19: asignado a una ruta Planificada, sale de ella y queda Reprogramado; la ruta conserva el historial del retiro.
    [Fact]
    public async Task En_una_ruta_planificada_sale_de_la_ruta_y_queda_reprogramado()
    {
        using var preparacion = Preparacion();
        var envio = await EnvioAsync(preparacion, recibido: true);
        var fecha = new DateOnly(2038, 1, 1).AddDays(Interlocked.Increment(ref _dia));
        var crear = await preparacion.PostAsJsonAsync("/api/planificacion/rutas",
            new CrearRutaRequest(fecha, Repartidor, Vehiculo, [envio.Id]));
        Assert.True(crear.IsSuccessStatusCode, await crear.Content.ReadAsStringAsync());
        Assert.Equal(EstadoEnvio.AsignadoARuta, (await LeerAsync(envio.Id)).Estado);
        using var publico = Publico();

        var respuesta = await ReprogramarAsync(publico, Token(envio.Id), EnTresDias, FranjaTarde);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(EstadoEnvio.Reprogramado, (await LeerAsync(envio.Id)).Estado);
        using var scope = factory.Services.CreateScope();
        var planificacion = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        Assert.False(await planificacion.EnviosParada.IgnoreQueryFilters().AnyAsync(p => p.EnvioId == envio.Id));
    }

    // Paso 2 y A2: las opciones respetan la anticipación (nada de hoy con 24 h); pedir hoy se rechaza y no cambia nada.
    [Fact]
    public async Task Las_opciones_respetan_la_anticipacion_y_pedir_sin_ella_se_rechaza()
    {
        using var preparacion = Preparacion();
        var envio = await EnvioAsync(preparacion, recibido: true);
        using var publico = Publico();
        var token = Token(envio.Id);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        var opciones = await publico.GetFromJsonAsync<OpcionesReprogramacionResponse>($"/api/seguimiento/{token}/reprogramacion");
        var rechazo = await VerificarAsync(publico, token, hoy, FranjaTarde);

        Assert.NotEmpty(opciones!.Opciones);
        Assert.DoesNotContain(opciones.Opciones, o => o.Fecha <= hoy.AddDays(-1));
        Assert.Contains(opciones.Opciones, o => o.Fecha == EnTresDias && o.FranjaId == FranjaTarde);
        Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
        Assert.Contains("ya no está disponible", await rechazo.Content.ReadAsStringAsync());
        Assert.Equal(EstadoEnvio.EnDeposito, (await LeerAsync(envio.Id)).Estado);
    }

    // Un enlace alterado no reprograma nada y responde como cualquier enlace inválido (404).
    [Fact]
    public async Task Un_enlace_invalido_no_reprograma()
    {
        using var publico = Publico();

        var verificacion = await VerificarAsync(publico, "token-inventado", EnTresDias, FranjaTarde);
        var respuesta = await ConfirmarAsync(publico, "token-inventado", EnTresDias, FranjaTarde, DigitosTelefono);

        Assert.Equal(HttpStatusCode.NotFound, verificacion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    // Requerimiento especial 4: con el enlace solo no se reprograma; hace falta pedir la verificación antes.
    [Fact]
    public async Task Sin_pedir_la_verificacion_no_reprograma()
    {
        using var preparacion = Preparacion();
        var envio = await EnvioAsync(preparacion, recibido: true);
        using var publico = Publico();

        var respuesta = await ConfirmarAsync(publico, Token(envio.Id), EnTresDias, FranjaTarde, DigitosTelefono);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("Primero pedí el código", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(EstadoEnvio.EnDeposito, (await LeerAsync(envio.Id)).Estado);
    }

    // Sin correo: se piden los últimos 4 dígitos del teléfono. Tres errores agotan la verificación y ni los dígitos
    // correctos sirven después; hay que pedir otra.
    [Fact]
    public async Task Sin_correo_se_piden_los_digitos_del_telefono_y_tres_errores_la_agotan()
    {
        using var preparacion = Preparacion();
        var envio = await EnvioAsync(preparacion, recibido: true);
        using var publico = Publico();
        var token = Token(envio.Id);

        var pedido = await VerificarAsync(publico, token, EnTresDias, FranjaTarde);
        var verificacion = await pedido.Content.ReadFromJsonAsync<VerificacionReprogramacionResponse>();
        Assert.Equal("Telefono", verificacion!.Medio);
        Assert.Null(verificacion.Destino);
        Assert.Empty(_correos.Enviados);

        var primero = await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, "0000");
        await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, "1111");
        await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, "2222");
        var tarde = await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, DigitosTelefono);

        Assert.Contains("Te quedan 2 intentos", await primero.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tarde.StatusCode);
        Assert.Contains("Superaste los intentos", await tarde.Content.ReadAsStringAsync());
        Assert.Equal(EstadoEnvio.EnDeposito, (await LeerAsync(envio.Id)).Estado);
    }

    // Con correo: el código llega al correo, que se muestra enmascarado; un código incorrecto no reprograma, el correcto
    // sí, y después llega el aviso de que se cambió la entrega. El código vale para la fecha elegida y una sola vez.
    [Fact]
    public async Task Con_correo_reprograma_con_el_codigo_que_llega_y_avisa_el_cambio()
    {
        using var preparacion = Preparacion();
        var envio = await EnvioAsync(preparacion, recibido: true, email: "ana.cu61@example.com");
        using var publico = Publico();
        var token = Token(envio.Id);

        var pedido = await VerificarAsync(publico, token, EnTresDias, FranjaTarde);
        var verificacion = await pedido.Content.ReadFromJsonAsync<VerificacionReprogramacionResponse>();
        var correo = Assert.Single(_correos.Enviados);
        var codigo = correo.Titulo[^6..];

        Assert.Equal("Correo", verificacion!.Medio);
        Assert.Equal("a*******@example.com", verificacion.Destino);
        Assert.Equal("ana.cu61@example.com", correo.Para);
        Assert.Matches("^[0-9]{6}$", codigo);

        var incorrecto = await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, codigo == "000000" ? "111111" : "000000");
        var otraFecha = await ConfirmarAsync(publico, token, EnTresDias.AddDays(1), FranjaTarde, codigo);
        Assert.Equal(HttpStatusCode.BadRequest, incorrecto.StatusCode);
        Assert.Contains("otra fecha", await otraFecha.Content.ReadAsStringAsync());
        Assert.Equal(EstadoEnvio.EnDeposito, (await LeerAsync(envio.Id)).Estado);

        var respuesta = await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, codigo);
        var repetido = await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, codigo);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(EstadoEnvio.Reprogramado, (await LeerAsync(envio.Id)).Estado);
        Assert.Contains("ya se usó", await repetido.Content.ReadAsStringAsync());
        Assert.Equal(2, _correos.Enviados.Count);
        Assert.Contains(_correos.Enviados, m => m.Asunto == "Se cambió la entrega de tu envío" && m.Para == "ana.cu61@example.com");
    }

    // Pedir un código nuevo anula el anterior.
    [Fact]
    public async Task Un_codigo_nuevo_anula_el_anterior()
    {
        using var preparacion = Preparacion();
        var envio = await EnvioAsync(preparacion, recibido: true, email: "beto.cu61@example.com");
        using var publico = Publico();
        var token = Token(envio.Id);

        await VerificarAsync(publico, token, EnTresDias, FranjaTarde);
        var primero = _correos.Enviados.Last().Titulo[^6..];
        await VerificarAsync(publico, token, EnTresDias, FranjaTarde);
        var segundo = _correos.Enviados.Last().Titulo[^6..];

        // Si por azar los dos códigos coinciden, la primera parte no se puede comprobar.
        if (primero != segundo)
        {
            var viejo = await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, primero);
            Assert.Equal(HttpStatusCode.BadRequest, viejo.StatusCode);
        }

        var nuevo = await ConfirmarAsync(publico, token, EnTresDias, FranjaTarde, segundo);
        Assert.Equal(HttpStatusCode.NoContent, nuevo.StatusCode);
    }

    // Pasos 3 y 4 con el teléfono: pide la verificación y confirma con los dígitos.
    private static async Task<HttpResponseMessage> ReprogramarAsync(HttpClient client, string token, DateOnly fecha, Guid franja)
    {
        var verificacion = await VerificarAsync(client, token, fecha, franja);
        Assert.True(verificacion.IsSuccessStatusCode, await verificacion.Content.ReadAsStringAsync());
        return await ConfirmarAsync(client, token, fecha, franja, DigitosTelefono);
    }

    private static Task<HttpResponseMessage> VerificarAsync(HttpClient client, string token, DateOnly fecha, Guid franja) =>
        client.PostAsJsonAsync($"/api/seguimiento/{token}/reprogramacion/verificacion",
            new SolicitarVerificacionRequest(fecha, franja));

    private static Task<HttpResponseMessage> ConfirmarAsync(HttpClient client, string token, DateOnly fecha, Guid franja,
        string codigo) =>
        client.PostAsJsonAsync($"/api/seguimiento/{token}/reprogramacion",
            new SolicitarReprogramacionRequest(fecha, franja, codigo));

    private string Token(Guid envioId) =>
        factory.Services.GetRequiredService<TokenSeguimiento>().Generar(DatosIniciales.OperadorDemoId, envioId);

    // Un envío del comercio demo en Montevideo (CP 11300), creado por la API y, si se pide, recibido en el depósito.
    private static async Task<CrearEnvioResponse> EnvioAsync(HttpClient client, bool recibido, string? email = null)
    {
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana CU61", "099123456", email, null),
            new DireccionRequest("Av. Italia", Guid.NewGuid().ToString("N")[..8], "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        Assert.True(creado.IsSuccessStatusCode, await creado.Content.ReadAsStringAsync());
        var envio = (await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        if (recibido)
        {
            (await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest(envio.Numero + "-1", null)))
                .EnsureSuccessStatusCode();
        }

        return envio;
    }

    private async Task<Envio> LeerAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        return await db.Envios.IgnoreQueryFilters().AsNoTracking().Include(e => e.Eventos).SingleAsync(e => e.Id == id);
    }

    private async Task<List<string>> MensajesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        return await db.Outbox.IgnoreQueryFilters().Where(m => m.Tipo == "EnvioReprogramado.v1").Select(m => m.Contenido).ToListAsync();
    }

    // Guarda los correos en lugar de mandarlos.
    private sealed class EmisorFalso : IEmisorCorreo
    {
        public ConcurrentQueue<MensajeCorreo> Enviados { get; } = new();

        public Task<ResultadoEnvioCorreo> EnviarAsync(MensajeCorreo mensaje, CancellationToken ct)
        {
            Enviados.Enqueue(mensaje);
            return Task.FromResult(new ResultadoEnvioCorreo(true, null));
        }
    }
}
