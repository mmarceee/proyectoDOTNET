using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Application.Features.IniciarRendicion;
using Logistica.Modules.Ejecucion.Contracts;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Ejecucion.Domain.Cargas;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Application;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Ejecucion;

public sealed class IniciarRendicionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int dia;
    private sealed record Datos(Guid Ruta, long Revision, Envio[] Envios, DateOnly Fecha);
    private IServiceScope Scope(Guid? operador = null)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("operador_id", (operador ?? DatosIniciales.OperadorDemoId).ToString())], "Prueba")) };
        return scope;
    }
    private async Task<Datos> PrepararAsync(bool cargada = true)
    {
        using var inicial = factory.CreateClient(); await inicial.GetAsync("/health");
        using var scope = Scope();
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-5);
        var fecha = new DateOnly(2025, 6, 1).AddDays(Interlocked.Increment(ref dia));
        var envios = Enumerable.Range(0, 2).Select(i =>
        {
            var e = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, $"CU54-{Guid.NewGuid():N}",
                new Destinatario("Prueba Rendición", "099123456"), new Direccion("Calle", "10", "Montevideo", "Montevideo", "11300"),
                [new DatosBulto(1, 10, 10, 10, 0), new DatosBulto(1, 10, 10, 10, 0)], OrigenEvento.PortalComercio, null, ahora);
            e.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
            e.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
            if (cargada) e.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
            return e;
        }).ToArray();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        db.Envios.AddRange(envios); await db.SaveChangesAsync();
        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, fecha, DatosIniciales.RepartidorDemoId,
            Guid.Parse("51111111-1111-1111-1111-111111111111"), envios.Select(e => new EnvioRuta(e.Id, e.Numero, fecha,
                Guid.Parse("61111111-1111-1111-1111-111111111111"), null, false,
                e.Bultos.Select(b => new CargaBulto(e.Id, b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToArray(),
                new PuntoEntrega("Calle", "10", "Montevideo", "Montevideo", "11300"))).ToArray(), ahora);
        ruta.Despachar(ahora); if (cargada) ruta.RegistrarCarga(true);
        var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        plan.Rutas.Add(ruta); await plan.SaveChangesAsync();
        return new(ruta.Id, ruta.Revision, envios, fecha);
    }
    private HttpClient Cliente() => factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false")).CreateClient(Sesion.Opciones);
    private async Task<MotivoNoEntrega> MotivoAusenteAsync(Envio envio)
    {
        using var scope = Scope();
        var reglas = scope.ServiceProvider.GetRequiredService<IReglasDeEntrega>();
        var hoja = await reglas.ParaHojaAsync(envio, default);
        var motivoId = hoja.Motivos.Single(m => m.Codigo == "AUSENTE").Id;
        var motivo = await reglas.MotivoAsync(envio, motivoId, default);
        Assert.NotNull(motivo);
        return motivo;
    }
    private async Task<OperacionDispositivoRequest> OperacionAsync(Datos d)
    {
        using var scope = Scope();
        var baseActual = await scope.ServiceProvider.GetRequiredService<PrepararRendicion>()
            .ConsultarAsync(d.Ruta, d.Envios.Select(e => e.Id).ToArray(), default);
        return new(Guid.CreateVersion7(), Guid.NewGuid(), 2, "IniciarRendicion", d.Ruta, null, DateTimeOffset.UtcNow,
            null, null, "00-123456789012345678901234567890ab-12345678901234ab-01",
            Rendicion: new(baseActual.Bultos.Select(b => new DeclaracionBultoRequest(b.BultoId, b.Situacion,
                b.RequiereDeclaracion, null)).ToArray(), [], "Regreso al depósito"));
    }
    private static async Task<ResultadoOperacionResponse> Enviar(HttpClient client, OperacionDispositivoRequest op)
    {
        var r = await client.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([op]));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados.Single();
    }
    [Fact]
    public async Task Declara_sin_recibir_ni_cambiar_envios_ni_finalizar_y_se_recupera_sin_duplicar()
    {
        var d = await PrepararAsync(); using var client = Cliente();
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var op = await OperacionAsync(d);
        op = op with { Rendicion = op.Rendicion! with { Bultos = op.Rendicion.Bultos.Select((b, i) => i == 0
            ? b with { Devuelve = false, Observacion = "Quedó retenido en el destino" } : b).ToArray() } };
        Assert.Equal("Aplicada", (await Enviar(client, op)).Resultado);
        Assert.Equal("YaAplicada", (await Enviar(client, op)).Resultado);
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
        var rendicion = await db.Rendiciones.Include(r => r.Lineas).SingleAsync(r => r.RutaId == d.Ruta);
        Assert.Equal(4, rendicion.Lineas.Count);
        Assert.Null(rendicion.FinalizadaEn);
        Assert.All(rendicion.Lineas, l => { Assert.Null(l.Recibida); Assert.Null(l.EstadoFinal); });
        Assert.Single(rendicion.Lineas, l => !l.Declarada && l.Observacion is not null);
        var ruta = await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas.SingleAsync(r => r.Id == d.Ruta);
        Assert.Equal(EstadoRuta.EnCurso, ruta.Estado); Assert.True(ruta.ReservaActiva); Assert.Equal(d.Revision, ruta.Revision);
        var ids = d.Envios.Select(e => e.Id).ToArray();
        Assert.All(await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Envios.Where(e => ids.Contains(e.Id)).ToListAsync(),
            e => Assert.Equal(EstadoEnvio.EnTransito, e.Estado));
        using var respuesta = await client.GetAsync($"/api/repartidor/rutas/{d.Ruta}/rendicion");
        respuesta.EnsureSuccessStatusCode(); Assert.True(respuesta.Headers.CacheControl?.NoStore);
        Assert.Equal(rendicion.Id, (await respuesta.Content.ReadFromJsonAsync<RendicionResponse>())!.Id);
        var api = scope.ServiceProvider.GetRequiredService<IEjecucionModuleApi>();
        Assert.Equal(rendicion.Id, (await api.ConsultarRendicionAsync(d.Ruta, default))!.Id);
        using var otro = Scope(Guid.NewGuid());
        Assert.Null(await otro.ServiceProvider.GetRequiredService<IEjecucionModuleApi>().ConsultarRendicionAsync(d.Ruta, default));
    }
    [Theory]
    [InlineData("observacion", "DeclaracionIncompleta")]
    [InlineData("omitido", "ManifiestoModificado")]
    [InlineData("ajeno", "ManifiestoModificado")]
    [InlineData("duplicado", "RendicionInvalida")]
    [InlineData("situacion", "ManifiestoModificado")]
    [InlineData("dependencia", "OperacionPreviaPendiente")]
    [InlineData("despachada", "RutaNoEnCurso")]
    [InlineData("finalizada", "RutaNoAsignada")]
    [InlineData("otro-operador", "RutaNoAsignada")]
    public async Task Rechaza_declaracion_sin_crear_rendicion(string caso, string motivo)
    {
        var d = await PrepararAsync(caso != "despachada"); var op = await OperacionAsync(d);
        using var client = Cliente();
        await Sesion.IngresarAsync(client, caso == "otro-operador" ? "repartidor@envios-del-este.uy" : "repartidor@operador-demo.uy");
        var bultos = op.Rendicion!.Bultos.ToArray();
        if (caso == "observacion") bultos[0] = bultos[0] with { Devuelve = false };
        if (caso == "ajeno") bultos[0] = bultos[0] with { BultoId = Guid.NewGuid() };
        if (caso == "situacion") bultos[0] = bultos[0] with { Situacion = "EntregadoEnCalle" };
        op = op with { Rendicion = op.Rendicion with { Bultos = caso == "omitido" ? bultos.Skip(1).ToArray()
            : caso == "duplicado" ? [bultos[0], bultos[0]] : bultos,
            Dependencias = caso == "dependencia" ? [Guid.NewGuid()] : [] } };
        if (caso == "finalizada")
        {
            using var s = Scope(); var plan = s.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            (await plan.Rutas.SingleAsync(r => r.Id == d.Ruta)).Finalizar(); await plan.SaveChangesAsync();
        }
        Assert.Equal(motivo, (await Enviar(client, op)).Motivo);
        using var scope = Scope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones.AnyAsync(r => r.RutaId == d.Ruta));
    }
    [Fact]
    public async Task Carga_pendiente_se_aplica_antes_de_rendir_sin_rechazar_por_la_revision_que_ella_cambio()
    {
        var d = await PrepararAsync(false); using var client = Cliente(); await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var ahora = new DateTimeOffset(d.Fecha.ToDateTime(new TimeOnly(15,0)),TimeSpan.Zero);
        var carga = new OperacionDispositivoRequest(Guid.CreateVersion7(), Guid.NewGuid(), 1, "ConfirmarCarga", d.Ruta, null, ahora,
            null, null, Carga: new(d.Revision, d.Envios.SelectMany(e => e.Bultos).Select(b => new LecturaCargaRequest(b.Codigo, ahora)).ToArray()));
        var op = await OperacionAsync(d);
        op = op with { Rendicion = op.Rendicion! with { Bultos = op.Rendicion.Bultos.Select(b => b with { Situacion = "NoIntentado", Devuelve = true }).ToArray(), Dependencias = [carga.OperacionId] } };
        var r = await client.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([op, carga])); r.EnsureSuccessStatusCode();
        Assert.All((await r.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados, x => Assert.Equal("Aplicada", x.Resultado));
    }
    [Fact]
    public async Task Rechazo_de_un_lote_anterior_impide_rendir_y_una_rendicion_iniciada_impide_nuevas_cargas()
    {
        var d = await PrepararAsync(); using var client = Cliente(); await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var op = await OperacionAsync(d);
        var entregaInvalida = op with { OperacionId = Guid.CreateVersion7(), Secuencia = 1, Tipo = "RegistrarEntrega", EnvioId = d.Envios[0].Id, Rendicion = null };
        Assert.Equal("Invalida", (await Enviar(client, entregaInvalida)).Resultado);
        Assert.Equal("DependeDeOperacionRechazada", (await Enviar(client, op)).Motivo);

        var otra = await PrepararAsync(); var rendir = await OperacionAsync(otra);
        Assert.Equal("Aplicada", (await Enviar(client, rendir)).Resultado);
        var carga = rendir with { OperacionId = Guid.CreateVersion7(), Tipo = "ConfirmarCarga", Rendicion = null };
        Assert.Equal("RutaEnRendicion", (await Enviar(client, carga)).Motivo);
    }
    [Fact]
    public async Task Dos_confirmaciones_concurrentes_crean_una_sola_rendicion()
    {
        var d = await PrepararAsync(); using var uno = Cliente(); using var dos = Cliente();
        await Sesion.IngresarAsync(uno, "repartidor@operador-demo.uy"); await Sesion.IngresarAsync(dos, "repartidor@operador-demo.uy");
        var op = await OperacionAsync(d);
        var resultados = await Task.WhenAll(Enviar(uno, op), Enviar(dos, op with { OperacionId = Guid.CreateVersion7() }));
        Assert.Single(resultados, r => r.Resultado == "Aplicada");
        Assert.Single(resultados, r => r.Motivo == "RendicionYaIniciada");
        using var scope = Scope(); Assert.Single(await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones.Where(r => r.RutaId == d.Ruta).ToListAsync());
    }
    [Fact]
    public async Task Carga_parcial_distingue_bulto_leido_de_bulto_que_nunca_salio()
    {
        var d = await PrepararAsync(false); using var scope = Scope();
        var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        (await plan.Rutas.SingleAsync(r => r.Id == d.Ruta)).RegistrarCarga(true); await plan.SaveChangesAsync();
        var db = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
        var bulto = d.Envios[0].Bultos[0];
        db.EscaneosCarga.Add(EscaneoCarga.Crear(DatosIniciales.OperadorDemoId, Guid.NewGuid(), d.Ruta, bulto.Id, bulto.Codigo, true, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var op = await OperacionAsync(d);
        Assert.Single(op.Rendicion!.Bultos, b => b.Situacion == "CargaParcial");
        Assert.Equal(3, op.Rendicion.Bultos.Count(b => b.Situacion == "NoCargado" && !b.Devuelve));
        using var client = Cliente(); await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        Assert.Equal("Aplicada", (await Enviar(client, op)).Resultado);
    }

    [Fact]
    public async Task Todos_entregados_admite_rendir_sin_devoluciones_y_no_confunde_intentos_anteriores_con_esta_ruta()
    {
        var d = await PrepararAsync(); using var client = Cliente(); await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD8kAAAAASUVORK5CYII=";
        foreach (var envio in d.Envios)
        {
            var entrega = new OperacionDispositivoRequest(Guid.CreateVersion7(), Guid.NewGuid(), 1, "RegistrarEntrega",
                d.Ruta, envio.Id, DateTimeOffset.UtcNow, -34.9m, -56.16m, Entrega: new(new("image/png", png)));
            Assert.Equal("Aplicada", (await Enviar(client, entrega)).Resultado);
        }
        var op = await OperacionAsync(d);
        Assert.All(op.Rendicion!.Bultos, b => { Assert.Equal("EntregadoEnCalle", b.Situacion); Assert.False(b.Devuelve); });
        Assert.Equal("Aplicada", (await Enviar(client, op)).Resultado);
    }

    [Fact]
    public async Task Intento_de_esta_ruta_se_distingue_del_historico_y_rendir_no_repite_T9()
    {
        var d = await PrepararAsync(); using var client = Cliente(); await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var intento = new OperacionDispositivoRequest(Guid.CreateVersion7(), Guid.NewGuid(), 1, "RegistrarIntentoFallido",
            d.Ruta, d.Envios[0].Id, DateTimeOffset.UtcNow, -34.9m, -56.16m,
            IntentoFallido: new((await MotivoAusenteAsync(d.Envios[0])).Id));
        Assert.Equal("Aplicada", (await Enviar(client, intento)).Resultado);
        using (var s = Scope())
        {
            var envios = s.ServiceProvider.GetRequiredService<EnviosDbContext>();
            var otro = await envios.Envios.Include(e => e.Intentos).SingleAsync(e => e.Id == d.Envios[1].Id);
            var antes = DateTimeOffset.UtcNow.AddDays(-2);
            otro.RegistrarIntentoFallido(await MotivoAusenteAsync(otro), null, null, new(-34.9m, -56.16m),
                new(false, false, 3, TimeSpan.FromDays(1)), null, antes);
            otro.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, antes.AddDays(1));
            otro.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, antes.AddDays(1));
            await envios.SaveChangesAsync();
        }
        var op = await OperacionAsync(d);
        Assert.All(op.Rendicion!.Bultos.Where(b => d.Envios[0].Bultos.Any(e => e.Id == b.BultoId)), b => Assert.Equal("IntentoFallido", b.Situacion));
        Assert.All(op.Rendicion.Bultos.Where(b => d.Envios[1].Bultos.Any(e => e.Id == b.BultoId)), b => Assert.Equal("NoIntentado", b.Situacion));
        Assert.Equal("Aplicada", (await Enviar(client, op)).Resultado);
        using var scope = Scope(); var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var fallido = await db.Envios.Include(e => e.Intentos).SingleAsync(e => e.Id == d.Envios[0].Id);
        Assert.Equal(EstadoEnvio.Reprogramado, fallido.Estado); Assert.Single(fallido.Intentos);
    }

    private sealed class OperacionQueFallaAlGuardar(EjecucionDbContext db) : IOperacionRepository
    {
        private readonly OperacionRepository original = new(db);
        public Task<OperacionDispositivo?> ObtenerAsync(Guid id, CancellationToken ct) => original.ObtenerAsync(id, ct);
        public void Agregar(OperacionDispositivo o) => original.Agregar(o);
        public async Task GuardarCambiosAsync(CancellationToken ct)
        {
            await original.GuardarCambiosAsync(ct);
            throw new InvalidOperationException("Fallo provocado antes del commit de rendición.");
        }
    }
    [Fact]
    public async Task Fallo_antes_del_commit_revierte_rendicion_lineas_y_operacion_y_permite_reintentar()
    {
        var d = await PrepararAsync(); var op = await OperacionAsync(d);
        using var api = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IOperacionRepository, OperacionQueFallaAlGuardar>()));
        using var client = api.CreateClient(Sesion.Opciones); await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var respuesta = await client.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([op]));
        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
            Assert.False(await db.Rendiciones.AnyAsync(r => r.RutaId == d.Ruta));
            Assert.False(await db.Operaciones.AnyAsync(o => o.Id == op.OperacionId));
        }
        using var recuperado = Cliente(); await Sesion.IngresarAsync(recuperado, "repartidor@operador-demo.uy");
        Assert.Equal("Aplicada", (await Enviar(recuperado, op)).Resultado);
    }
}
