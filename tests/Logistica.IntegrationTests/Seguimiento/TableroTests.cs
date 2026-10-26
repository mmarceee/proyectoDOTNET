using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-66, foto inicial: GET /api/tablero con sesión real y el respaldo provisorio apagado.
public class TableroTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid ZonaDemo = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly Guid FranjaTardeDemo = Guid.Parse("72222222-2222-2222-2222-222222222222"); // 14 a 18, todos los días
    private static readonly TimeZoneInfo Montevideo = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    // Requerimiento especial 3: un envío pendiente cuya ventana terminó ayer está en riesgo (vencido); uno programado
    // para la semana que viene, no.
    [Fact]
    public async Task El_tablero_muestra_en_riesgo_el_envio_vencido_y_no_el_de_la_semana_que_viene()
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Montevideo).DateTime);
        var vencido = await EnvioAsync(DatosIniciales.OperadorDemoId, hoy.AddDays(-1));
        var futuro = await EnvioAsync(DatosIniciales.OperadorDemoId, hoy.AddDays(7));
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var tablero = await despacho.GetFromJsonAsync<TableroResponse>("/api/tablero");

        var enRiesgo = Assert.Single(tablero!.EnRiesgo, e => e.Numero == vencido.Numero);
        Assert.True(enRiesgo.Vencido);
        Assert.True(enRiesgo.MinutosRestantes < 0);
        Assert.DoesNotContain(tablero.EnRiesgo, e => e.Numero == futuro.Numero);
    }

    // Aislamiento (ADR-0002): el despachador de otro operador no ve los envíos en riesgo de Demo.
    [Fact]
    public async Task El_despachador_de_otro_operador_no_ve_los_envios_de_demo()
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Montevideo).DateTime);
        var deDemo = await EnvioAsync(DatosIniciales.OperadorDemoId, hoy.AddDays(-1));
        using var este = Cliente();
        await Sesion.IngresarAsync(este, "despacho@envios-del-este.uy");

        var tablero = await este.GetFromJsonAsync<TableroResponse>("/api/tablero");

        Assert.DoesNotContain(tablero!.EnRiesgo, e => e.Numero == deDemo.Numero);
    }

    // Pre-condición 1: sólo Despachador o Administrador; sin sesión, 401.
    [Fact]
    public async Task Solo_el_despachador_o_el_administrador_ven_el_tablero()
    {
        using var admin = Cliente();
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");
        using var deposito = Cliente();
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        using var anonimo = Cliente();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/tablero")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deposito.GetAsync("/api/tablero")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/tablero")).StatusCode);
    }

    // Paso 1: la página del tablero y su ítem de menú, sólo para Despachador o Administrador.
    [Fact]
    public async Task La_pagina_del_tablero_es_para_el_despachador_y_no_para_el_operario_de_deposito()
    {
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");
        using var deposito = Cliente();
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");

        var pagina = await despacho.GetStringAsync("/backoffice/tablero");
        Assert.Contains("id=\"mapa\"", pagina);
        Assert.Contains("/hubs/tablero", pagina);
        Assert.Contains("href=\"/backoffice/tablero\"", await despacho.GetStringAsync("/backoffice"));
        Assert.DoesNotContain("href=\"/backoffice/tablero\"", await deposito.GetStringAsync("/backoffice"));
        Assert.Equal(HttpStatusCode.Forbidden, (await deposito.GetAsync("/backoffice/tablero")).StatusCode);
    }

    // ADR-0004, sección 2.5: un conflicto de sincronización aparece en el tablero hasta que el despachador lo revisa; el
    // de otro operador no lo encuentra y el operario de depósito no puede revisarlo.
    [Fact]
    public async Task Un_conflicto_aparece_hasta_que_el_despachador_lo_revisa()
    {
        var operacionId = await ConflictoAsync(DatosIniciales.OperadorDemoId);
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");
        using var este = Cliente();
        await Sesion.IngresarAsync(este, "despacho@envios-del-este.uy");
        using var deposito = Cliente();
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        var revision = $"/api/tablero/conflictos/{operacionId}/revision";

        var antes = await despacho.GetFromJsonAsync<TableroResponse>("/api/tablero");
        var conflicto = Assert.Single(antes!.Conflictos, c => c.OperacionId == operacionId);
        Assert.Equal("Repartidor Demo 1", conflicto.Repartidor);
        Assert.Equal("EnvioExtraviado", conflicto.Motivo);

        Assert.Equal(HttpStatusCode.Forbidden, (await deposito.PostAsync(revision, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await este.PostAsync(revision, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await despacho.PostAsync(revision, null)).StatusCode);

        var despues = await despacho.GetFromJsonAsync<TableroResponse>("/api/tablero");
        Assert.DoesNotContain(despues!.Conflictos, c => c.OperacionId == operacionId);
    }

    // Una entrega de la PWA rechazada porque el envío ya estaba extraviado, guardada con el operador indicado.
    private async Task<Guid> ConflictoAsync(Guid operadorId)
    {
        factory.CreateClient();
        var datos = new DatosOperacion(operadorId, DatosIniciales.RepartidorDemoId, Guid.NewGuid(), 1, "RegistrarEntrega",
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, false);
        var operacion = OperacionDispositivo.Rechazada(Guid.NewGuid(), datos, ResultadoOperacion.Conflicto,
            "EnvioExtraviado", null, "{}");

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EjecucionDbContext>(scope.ServiceProvider,
            new InquilinoFijo(operadorId, null));
        db.Operaciones.Add(operacion);
        await db.SaveChangesAsync();
        return operacion.Id;
    }

    // Un envío en el depósito con fecha y franja de la tarde (14 a 18), guardado con el operador indicado.
    private async Task<Envio> EnvioAsync(Guid operadorId, DateOnly fecha)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        var ahora = DateTimeOffset.UtcNow;
        var envio = Envio.Crear(operadorId, DatosIniciales.ComercioDemoId, $"ENV-T-{Guid.NewGuid():N}".ToUpperInvariant(),
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, ahora.AddDays(-3));
        envio.DefinirPlanificacion(ZonaDemo, FranjaTardeDemo, fecha);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora.AddDays(-2));

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(operadorId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }
}
