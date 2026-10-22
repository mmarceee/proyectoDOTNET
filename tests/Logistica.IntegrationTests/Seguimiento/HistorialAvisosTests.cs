using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Avisos;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Application.Features.HistorialAvisos;
using Logistica.Modules.Seguimiento.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

public sealed class HistorialAvisosTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Ruta = "/api/avisos/entregas";
    private static readonly InquilinoFijo Demo = new(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId);
    private static readonly DateTimeOffset Ahora = new(2030, 1, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Historial_filtra_dias_del_operador_pagina_y_detalla_intentos_sin_exponer_secretos()
    {
        using var api = CrearApi();
        using var cliente = await ClienteAsync(api);
        var antes = await CrearAsync(api.Services, fecha: new(2031, 2, 3, 2, 59, 59, TimeSpan.Zero));
        var primero = await CrearAsync(api.Services, fecha: new(2031, 2, 3, 3, 0, 0, TimeSpan.Zero));
        for (var i = 1; i <= 20; i++) await CrearAsync(api.Services, fecha: new(2031, 2, 3, 3, i, 0, TimeSpan.Zero));
        var despues = await CrearAsync(api.Services, fecha: new(2031, 2, 4, 3, 0, 0, TimeSpan.Zero));
        var filtro = "?desde=2031-02-03&hasta=2031-02-03&estado=EnColaDeFallidos";
        using var respuesta = await cliente.GetAsync(Ruta + filtro);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(respuesta.Headers.CacheControl!.NoStore);
        var pagina = (await respuesta.Content.ReadFromJsonAsync<HistorialAvisosResponse>())!;
        Assert.Equal(21, pagina.TotalRegistros);
        Assert.Equal(20, pagina.Entregas.Count);
        Assert.Equal(pagina.Entregas.OrderByDescending(e => e.FechaEvento).ThenByDescending(e => e.Id), pagina.Entregas);
        Assert.DoesNotContain(pagina.Entregas, e => e.Id == antes || e.Id == despues);
        Assert.All(pagina.Entregas, e => Assert.Equal(TimeSpan.FromHours(-3), e.FechaEvento.Offset));
        var segunda = (await cliente.GetFromJsonAsync<HistorialAvisosResponse>(Ruta + filtro + "&pagina=2"))!;
        Assert.Equal(primero, Assert.Single(segunda.Entregas).Id);
        using var detalleHttp = await cliente.GetAsync($"{Ruta}/{primero}");
        var json = await detalleHttp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secreto", json, StringComparison.OrdinalIgnoreCase);
        var detalle = JsonSerializer.Deserialize<DetalleEntregaAvisoResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(new int?[] { 500, 503 }, detalle.Intentos.Select(i => i.CodigoRespuesta));
        Assert.Equal(new[] { 1, 2 }, detalle.Intentos.Select(i => i.Numero));
        Assert.True(detalle.Entrega.PuedeReenviar);
        Assert.StartsWith("https://comercio.example/", detalle.UrlDestino);
    }

    [Theory]
    [InlineData("?pagina=0")]
    [InlineData("?estado=1")]
    [InlineData("?estado=Otro")]
    [InlineData("?desde=2030-01-03&hasta=2030-01-02")]
    [InlineData("?hasta=9999-12-31")]
    public async Task Rechaza_filtros_invalidos(string filtro)
    {
        using var api = CrearApi();
        using var cliente = await ClienteAsync(api);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync(Ruta + filtro)).StatusCode);
    }

    [Fact]
    public async Task Reenvio_individual_conserva_intentos_y_id_y_genera_un_solo_outbox_ante_dos_peticiones()
    {
        using var api = CrearApi();
        using var cliente = await ClienteAsync(api);
        var id = await CrearAsync(api.Services);
        // Fijar primero la cookie antifalsificación: ambas peticiones deben superar la protección HTTP.
        await cliente.GetAsync("/api/avisos/suscripciones/antifalsificacion");
        var respuestas = await Task.WhenAll(PostAsync(cliente, $"{Ruta}/{id}/reenvio"), PostAsync(cliente, $"{Ruta}/{id}/reenvio"));
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Accepted);
        var rechazada = Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.BadRequest);
        using var problema = JsonDocument.Parse(await rechazada.Content.ReadAsStringAsync());
        Assert.Contains("Sólo se reenvían", problema.RootElement.GetProperty("detail").GetString());
        await using var scope = api.Services.CreateAsyncScope();
        await using var db = Contexto(scope.ServiceProvider, Demo);
        var entrega = await db.EntregasAviso.Include(e => e.Intentos).SingleAsync(e => e.Id == id);
        Assert.Equal(EstadoEntregaAviso.Pendiente, entrega.Estado);
        Assert.Equal(2, entrega.Intentos.Count);
        Assert.Equal(id, Assert.Single(await PendientesAsync(db, id)).EntregaId);
        var detalle = await cliente.GetFromJsonAsync<DetalleEntregaAvisoResponse>($"{Ruta}/{id}");
        Assert.False(detalle!.Entrega.PuedeReenviar);
    }

    [Fact]
    public async Task Lote_valido_elimina_duplicados_y_encola_todas_las_entregas()
    {
        using var api = CrearApi();
        using var cliente = await ClienteAsync(api);
        var a = await CrearAsync(api.Services);
        var b = await CrearAsync(api.Services);
        using var respuesta = await PostAsync(cliente, Ruta + "/reenvios", new ReenviarAvisosRequest([b, a, b]));
        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        Assert.Equal(2, (await respuesta.Content.ReadFromJsonAsync<ReenvioAvisosResponse>())!.Cantidad);
        await using var scope = api.Services.CreateAsyncScope();
        await using var db = Contexto(scope.ServiceProvider, Demo);
        Assert.Equal(2, (await PendientesAsync(db, a, b)).Count);
        Assert.All(await db.EntregasAviso.Where(e => e.Id == a || e.Id == b).ToListAsync(), e => Assert.Equal(EstadoEntregaAviso.Pendiente, e.Estado));
    }

    [Theory]
    [InlineData("Pendiente", true)]
    [InlineData("Fallida", true)]
    [InlineData("Exitosa", true)]
    [InlineData("EnColaDeFallidos", false)]
    public async Task Lote_con_estado_no_reenviable_o_suscripcion_inactiva_no_encola_nada(string estado, bool activa)
    {
        using var api = CrearApi();
        using var cliente = await ClienteAsync(api);
        var valida = await CrearAsync(api.Services);
        var invalida = await CrearAsync(api.Services, estado: Enum.Parse<EstadoEntregaAviso>(estado), activa: activa);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(cliente, Ruta + "/reenvios", new ReenviarAvisosRequest([valida, invalida]))).StatusCode);
        await using var scope = api.Services.CreateAsyncScope();
        await using var db = Contexto(scope.ServiceProvider, Demo);
        Assert.Empty(await PendientesAsync(db, valida, invalida));
        Assert.Equal(EstadoEntregaAviso.EnColaDeFallidos, (await db.EntregasAviso.SingleAsync(e => e.Id == valida)).Estado);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_lista_detalla_ni_reenvia_entregas_de_otro_operador_o_comercio(bool otroOperador)
    {
        using var api = CrearApi();
        using var cliente = await ClienteAsync(api);
        var ajeno = new InquilinoFijo(otroOperador ? Guid.NewGuid() : Demo.OperadorId, otroOperador ? Demo.ComercioId : Guid.NewGuid());
        var id = await CrearAsync(api.Services, tenant: ajeno);
        var propia = await CrearAsync(api.Services);
        var lista = (await cliente.GetFromJsonAsync<HistorialAvisosResponse>(Ruta))!;
        Assert.DoesNotContain(lista.Entregas, e => e.Id == id);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"{Ruta}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(cliente, $"{Ruta}/{id}/reenvio")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(cliente, Ruta + "/reenvios", new ReenviarAvisosRequest([propia, id]))).StatusCode);
        await using var scope = api.Services.CreateAsyncScope();
        await using var db = Contexto(scope.ServiceProvider, Demo);
        Assert.Empty(await PendientesAsync(db, propia));
    }

    [Fact]
    public async Task Un_fallo_despues_de_SaveChanges_revierte_estado_y_outbox_juntos()
    {
        using var api = CrearApi();
        var id = await CrearAsync(api.Services);
        using var falla = api.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<ICurrentTenant>(_ => Demo);
            s.AddScoped<IReenviosAvisoRepository>(sp => new FalloAlGuardar(ActivatorUtilities.CreateInstance<ReenviosAvisoRepository>(sp)));
        }));
        await using (var scope = falla.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ReenviarAvisosHandler>().HandleAsync([id], default));
        await using var lectura = api.Services.CreateAsyncScope();
        await using var db = Contexto(lectura.ServiceProvider, Demo);
        Assert.Equal(EstadoEntregaAviso.EnColaDeFallidos, (await db.EntregasAviso.SingleAsync(e => e.Id == id)).Estado);
        Assert.Empty(await PendientesAsync(db, id));
    }

    [Fact]
    public async Task Exige_sesion_comercio_operador_y_token_antifalsificacion()
    {
        using var api = CrearApi();
        using var anonimo = api.CreateClient(Sesion.Opciones);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Ruta)).StatusCode);
        using var sinOperador = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(sinOperador, "comercio@tiendademo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await sinOperador.GetAsync(Ruta)).StatusCode);
        using var administrador = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(administrador, "admin@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync(Ruta)).StatusCode);
        using var cliente = await ClienteAsync(api);
        var id = await CrearAsync(api.Services);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsync($"{Ruta}/{id}/reenvio", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(cliente, Ruta + "/reenvios", new ReenviarAvisosRequest([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(cliente, Ruta + "/reenvios", new ReenviarAvisosRequest(Enumerable.Repeat(id, 51).ToArray()))).StatusCode);
    }

    private WebApplicationFactory<Program> CrearApi() => factory.WithWebHostBuilder(b =>
        b.UseSetting("Identity:PermitirTenantProvisorio", "false")
         .UseSetting("Seguimiento:Avisos:ClaveCifrado", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    private static async Task<HttpClient> ClienteAsync(WebApplicationFactory<Program> api)
    {
        var cliente = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(cliente, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(cliente, DatosIniciales.OperadorDemoId);
        return cliente;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient cliente, string ruta, object? body = null)
    {
        var token = (await cliente.GetFromJsonAsync<TokenAntifalsificacionResponse>("/api/avisos/suscripciones/antifalsificacion"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, ruta);
        request.Headers.Add("RequestVerificationToken", token.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await cliente.SendAsync(request);
    }

    private static async Task<Guid> CrearAsync(IServiceProvider servicios, DateTimeOffset? fecha = null,
        EstadoEntregaAviso estado = EstadoEntregaAviso.EnColaDeFallidos, bool activa = true, InquilinoFijo? tenant = null)
    {
        tenant ??= Demo;
        await using var scope = servicios.CreateAsyncScope();
        await using var db = Contexto(scope.ServiceProvider, tenant);
        var suscripcion = SuscripcionAviso.Crear(tenant.OperadorId!.Value, tenant.ComercioId!.Value, Guid.NewGuid(),
            $"https://comercio.example/{Guid.NewGuid()}", [TipoEventoAviso.CambioDeEstado], "secreto-cifrado-de-prueba");
        var envio = EnvioSeguido.Crear(tenant.OperadorId.Value, tenant.ComercioId.Value, Guid.NewGuid(), "ENV-CU65-" + Guid.NewGuid().ToString("N"));
        var entrega = EntregaAviso.Crear(suscripcion, Guid.NewGuid(), envio, "Entregado", TipoEventoAviso.CambioDeEstado, fecha ?? Ahora);
        if (estado != EstadoEntregaAviso.Pendiente)
        {
            entrega.RegistrarIntento(Ahora, false, 500, "Error del receptor", false);
            if (estado != EstadoEntregaAviso.Fallida)
                entrega.RegistrarIntento(Ahora.AddMinutes(1), estado == EstadoEntregaAviso.Exitosa, estado == EstadoEntregaAviso.Exitosa ? 200 : 503, null, true);
        }
        if (!activa) suscripcion.Desactivar();
        db.SuscripcionesAviso.Add(suscripcion);
        db.EnviosSeguidos.Add(envio);
        db.EntregasAviso.Add(entrega);
        await db.SaveChangesAsync();
        return entrega.Id;
    }

    private static SeguimientoDbContext Contexto(IServiceProvider servicios, InquilinoFijo tenant) =>
        ActivatorUtilities.CreateInstance<SeguimientoDbContext>(servicios, tenant);

    private static async Task<List<AvisoPendiente>> PendientesAsync(SeguimientoDbContext db, params Guid[] ids) =>
        (await db.Outbox.AsNoTracking().Where(m => m.Tipo == AvisoPendiente.Tipo).ToListAsync())
        .Select(m => JsonSerializer.Deserialize<AvisoPendiente>(m.Contenido)!).Where(m => ids.Contains(m.EntregaId)).ToList();

    private sealed class FalloAlGuardar(IReenviosAvisoRepository real) : IReenviosAvisoRepository
    {
        public Task<EntregaAviso?> BloquearEntregaAsync(Guid id, CancellationToken ct) => real.BloquearEntregaAsync(id, ct);
        public Task<SuscripcionAviso?> BloquearSuscripcionAsync(Guid id, CancellationToken ct) => real.BloquearSuscripcionAsync(id, ct);
        public async Task GuardarAsync(CancellationToken ct)
        {
            await real.GuardarAsync(ct);
            throw new InvalidOperationException("Fallo simulado antes de confirmar.");
        }
    }
}
