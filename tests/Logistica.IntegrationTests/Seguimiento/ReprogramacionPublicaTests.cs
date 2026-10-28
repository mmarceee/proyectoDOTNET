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
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-61 de punta a punta: la destinataria reprograma desde su enlace, sin usuario (el token fija el inquilino).
// Las reglas de Demo piden 24 horas de anticipación; la franja de la tarde (14 a 18) rige todos los días.
public class ReprogramacionPublicaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid FranjaTarde = Guid.Parse("72222222-2222-2222-2222-222222222222");
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly DateOnly EnTresDias = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
    private static int _dia;

    // El público: sin sesión y sin el respaldo provisorio, como en CU-60.
    private HttpClient Publico() => factory
        .WithWebHostBuilder(b => b
            .UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("Seguimiento:ConsultasPorMinuto", "1000"))
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
        var rechazo = await ReprogramarAsync(publico, token, hoy, FranjaTarde);

        Assert.NotEmpty(opciones!.Opciones);
        Assert.DoesNotContain(opciones.Opciones, o => o.Fecha <= hoy.AddDays(-1));
        Assert.Contains(opciones.Opciones, o => o.Fecha == EnTresDias && o.FranjaId == FranjaTarde);
        Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
        Assert.Contains("anticipación", await rechazo.Content.ReadAsStringAsync());
        Assert.Equal(EstadoEnvio.EnDeposito, (await LeerAsync(envio.Id)).Estado);
    }

    // Un enlace alterado no reprograma nada y responde como cualquier enlace inválido (404).
    [Fact]
    public async Task Un_enlace_invalido_no_reprograma()
    {
        using var publico = Publico();

        var respuesta = await ReprogramarAsync(publico, "token-inventado", EnTresDias, FranjaTarde);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    private static Task<HttpResponseMessage> ReprogramarAsync(HttpClient client, string token, DateOnly fecha, Guid franja) =>
        client.PostAsJsonAsync($"/api/seguimiento/{token}/reprogramacion", new SolicitarReprogramacionRequest(fecha, franja));

    private string Token(Guid envioId) =>
        factory.Services.GetRequiredService<TokenSeguimiento>().Generar(DatosIniciales.OperadorDemoId, envioId);

    // Un envío del comercio demo en Montevideo (CP 11300), creado por la API y, si se pide, recibido en el depósito.
    private static async Task<CrearEnvioResponse> EnvioAsync(HttpClient client, bool recibido)
    {
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana CU61", "099123456", null, null),
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
}
