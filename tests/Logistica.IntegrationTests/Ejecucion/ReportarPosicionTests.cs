using System.Net.Http.Json;
using System.Security.Claims;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Contracts;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Posiciones;
using Logistica.Modules.Ejecucion.Domain.Operaciones;

namespace Logistica.IntegrationTests.Ejecucion;

public sealed class ReportarPosicionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int dia;
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private IServiceScope Scope(Guid? operador = null)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("operador_id", (operador ?? DatosIniciales.OperadorDemoId).ToString())], "Prueba")) };
        return scope;
    }
    private async Task<Guid> Preparar(bool iniciar = true)
    {
        using var client = factory.CreateClient(); await client.GetAsync("/health");
        using var scope = Scope();
        var fecha = new DateOnly(2024, 1, 1).AddDays(Interlocked.Increment(ref dia));
        var envio = Guid.NewGuid();
        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, fecha, DatosIniciales.RepartidorDemoId, Vehiculo,
            [new EnvioRuta(envio, "GPS-TEST", fecha, Guid.NewGuid(), null, false,
                [new CargaBulto(envio, Guid.NewGuid(), 1, 10, 10, 10)], new PuntoEntrega("Calle", "1", "Montevideo", "Montevideo", "11300"))],
            DateTimeOffset.UtcNow.AddHours(-2));
        ruta.Despachar(DateTimeOffset.UtcNow.AddHours(-1)); if (iniciar) ruta.Iniciar();
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>(); db.Rutas.Add(ruta); await db.SaveChangesAsync();
        return ruta.Id;
    }
    private static OperacionDispositivoRequest Posicion(Guid ruta, DateTimeOffset? captura = null) =>
        new(Guid.CreateVersion7(), Guid.NewGuid(), 1, "ReportarPosicion", ruta, null,
            captura ?? DateTimeOffset.UtcNow.AddMinutes(-1), -34.9m, -56.16m);

    private async Task<HttpClient> Cliente(Observador? observador = null, string email = "repartidor@operador-demo.uy")
    {
        var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Identity:PermitirTenantProvisorio", "false");
            if (observador is not null) b.ConfigureServices(s => s.AddSingleton<IConsumidorPosicionReportada>(observador));
        });
        var client = host.CreateClient(Sesion.Opciones); await Sesion.IngresarAsync(client, email); return client;
    }
    private static async Task<ResultadoOperacionResponse> Enviar(HttpClient client, OperacionDispositivoRequest op)
    {
        var response = await client.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([op], 1));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados.Single();
    }

    [Fact]
    public async Task Guarda_una_vez_publica_despues_del_commit_y_la_ultima_no_retrocede_por_recepcion_tardia()
    {
        var ruta = await Preparar(); var observador = new Observador(factory.ConnectionString);
        using var client = await Cliente(observador);
        var nueva = Posicion(ruta); var vieja = Posicion(ruta, nueva.OcurridoEn.AddMinutes(-10));
        Assert.Equal("Aplicada", (await Enviar(client, nueva)).Resultado);
        Assert.Equal("YaAplicada", (await Enviar(client, nueva)).Resultado);
        Assert.Equal("Aplicada", (await Enviar(client, vieja)).Resultado);
        Assert.Equal(2, observador.Eventos.Count); Assert.All(observador.Eventos, e => Assert.False(e.SoloHistorial));
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
        Assert.Equal(2, await db.Posiciones.CountAsync(p => p.RutaId == ruta));
        var api = scope.ServiceProvider.GetRequiredService<IEjecucionModuleApi>();
        Assert.Equal(nueva.OperacionId, Assert.Single(await api.ConsultarUltimasPosicionesAsync([Vehiculo], default)).Id);
        using var otro = Scope(Guid.NewGuid());
        Assert.Empty(await otro.ServiceProvider.GetRequiredService<IEjecucionModuleApi>().ConsultarUltimasPosicionesAsync([Vehiculo], default));
    }

    [Theory]
    [InlineData("latitud", "PosicionInvalida")]
    [InlineData("longitud", "PosicionInvalida")]
    [InlineData("sin-fecha", "PosicionInvalida")]
    [InlineData("sin-coordenada", "PosicionInvalida")]
    [InlineData("con-envio", "PosicionInvalida")]
    [InlineData("antigua", "PosicionFueraDeRetencion")]
    [InlineData("antes-despacho", "PosicionFueraDeJornada")]
    [InlineData("sin-iniciar", "RutaNoEnCurso")]
    [InlineData("otro-operador", "RutaNoAsignada")]
    public async Task Rechaza_sin_guardar_posicion_ni_publicar(string caso, string motivo)
    {
        var ruta = await Preparar(caso != "sin-iniciar"); var op = Posicion(ruta);
        op = caso switch
        {
            "latitud" => op with { Latitud = 91 }, "longitud" => op with { Longitud = -181 },
            "sin-fecha" => op with { OcurridoEn = default }, "sin-coordenada" => op with { Latitud = null },
            "con-envio" => op with { EnvioId = Guid.NewGuid() },
            "antigua" => op with { OcurridoEn = DateTimeOffset.UtcNow.AddDays(-31) },
            "antes-despacho" => op with { OcurridoEn = DateTimeOffset.UtcNow.AddHours(-2) }, _ => op
        };
        var observador = new Observador(factory.ConnectionString);
        using var client = await Cliente(observador, caso == "otro-operador" ? "repartidor@envios-del-este.uy" : "repartidor@operador-demo.uy");
        Assert.Equal(motivo, (await Enviar(client, op)).Motivo); Assert.Empty(observador.Eventos);
        using var scope = Scope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Posiciones.AnyAsync(p => p.RutaId == ruta));
    }

    [Fact]
    public async Task Acepta_historial_anterior_a_rendicion_aun_finalizada_y_rechaza_capturas_posteriores()
    {
        var rutaId = await Preparar(); var vuelta = DateTimeOffset.UtcNow.AddMinutes(-5);
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
            db.Rendiciones.Add(Rendicion.Iniciar(DatosIniciales.OperadorDemoId, rutaId, DatosIniciales.RepartidorDemoId,
                Guid.NewGuid(), vuelta, null, [])); await db.SaveChangesAsync();
            var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            (await plan.Rutas.SingleAsync(r => r.Id == rutaId)).Finalizar(); await plan.SaveChangesAsync();
        }
        var observador = new Observador(factory.ConnectionString); using var client = await Cliente(observador);
        Assert.Equal("Aplicada", (await Enviar(client, Posicion(rutaId, vuelta.AddMinutes(-1)))).Resultado);
        Assert.True(Assert.Single(observador.Eventos).SoloHistorial);
        Assert.Equal("PosicionFueraDeJornada", (await Enviar(client, Posicion(rutaId, vuelta))).Motivo);
        Assert.Equal("PosicionFueraDeJornada", (await Enviar(client, Posicion(rutaId))).Motivo);
    }

    [Fact]
    public async Task Un_consumidor_fallido_no_revierte_la_posicion_y_el_reintento_no_vuelve_a_emitir()
    {
        var ruta = await Preparar(); var observador = new Observador(factory.ConnectionString) { Fallar = true };
        using var client = await Cliente(observador); var op = Posicion(ruta);
        Assert.Equal("Aplicada", (await Enviar(client, op)).Resultado);
        Assert.Equal("YaAplicada", (await Enviar(client, op)).Resultado); Assert.Single(observador.Eventos);
    }

    [Fact]
    public async Task Rechazo_y_aviso_Outbox_son_idempotentes_y_la_retencion_no_borra_su_evidencia()
    {
        var ruta = await Preparar(); using var client = await Cliente();
        var op = Posicion(ruta, DateTimeOffset.UtcNow.AddDays(-31));
        Assert.Equal("PosicionFueraDeRetencion", (await Enviar(client, op)).Motivo);
        Assert.Equal("PosicionFueraDeRetencion", (await Enviar(client, op)).Motivo);
        var limpieza = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<DepurarPosiciones>().Single();
        await limpieza.DepurarAsync(default);
        using var scope = Scope(); var db = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
        var aviso = Assert.Single(await db.Outbox.Where(m => m.Tipo == ConflictoSincronizacionRegistrado.Tipo
            && m.Contenido.Contains(op.OperacionId.ToString())).ToArrayAsync());
        Assert.Equal(DatosIniciales.OperadorDemoId, aviso.OperadorId);
        var rechazada = await db.Operaciones.SingleAsync(o => o.Id == op.OperacionId);
        Assert.NotNull(rechazada.Contenido); Assert.False(rechazada.Revisada);
    }

    [Fact]
    public async Task Depuracion_mantiene_el_filtro_Tenant_y_el_trabajo_recuerda_cada_operador_en_su_ambito()
    {
        await Preparar(); var vieja = DateTimeOffset.UtcNow.AddDays(-31); var nueva = DateTimeOffset.UtcNow;
        var vehiculo = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        using (var a = Scope())
        {
            var db = a.ServiceProvider.GetRequiredService<EjecucionDbContext>();
            db.Posiciones.Add(PosicionVehiculo.Crear(ids[0], DatosIniciales.OperadorDemoId, vehiculo, Guid.NewGuid(), 0, 0, vieja, nueva));
            db.Posiciones.Add(PosicionVehiculo.Crear(ids[1], DatosIniciales.OperadorDemoId, vehiculo, Guid.NewGuid(), 0, 0, nueva, nueva));
            await db.SaveChangesAsync();
        }
        using (var b = Scope(DatosIniciales.OperadorEsteId))
        {
            var db = b.ServiceProvider.GetRequiredService<EjecucionDbContext>();
            db.Posiciones.Add(PosicionVehiculo.Crear(ids[2], DatosIniciales.OperadorEsteId, vehiculo, Guid.NewGuid(), 0, 0, vieja, nueva));
            await db.SaveChangesAsync();
        }
        using (var a = Scope())
            await a.ServiceProvider.GetRequiredService<IPosicionRepository>().EliminarAnterioresAsync(nueva.AddDays(-30), default);
        using (var b = Scope(DatosIniciales.OperadorEsteId))
            Assert.True(await b.ServiceProvider.GetRequiredService<EjecucionDbContext>().Posiciones.AnyAsync(p => p.Id == ids[2]));
        var limpieza = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<DepurarPosiciones>().Single();
        await limpieza.DepurarAsync(default);
        using (var b = Scope(DatosIniciales.OperadorEsteId))
            Assert.False(await b.ServiceProvider.GetRequiredService<EjecucionDbContext>().Posiciones.AnyAsync(p => p.Id == ids[2]));
        using (var a = Scope())
        {
            var db = a.ServiceProvider.GetRequiredService<EjecucionDbContext>();
            Assert.False(await db.Posiciones.AnyAsync(p => p.Id == ids[0]));
            Assert.True(await db.Posiciones.AnyAsync(p => p.Id == ids[1]));
        }
    }

    private sealed class Observador(string connectionString) : IConsumidorPosicionReportada
    {
        public List<PosicionReportada> Eventos { get; } = [];
        public bool Fallar { get; init; }
        public async Task ConsumirAsync(PosicionReportada evento, CancellationToken ct)
        {
            // Otra conexión sólo puede ver la fila una vez confirmado el commit.
            await using var conexion = new NpgsqlConnection(connectionString); await conexion.OpenAsync(ct);
            await using var sql = new NpgsqlCommand("SELECT count(*) FROM ejecucion.posiciones_vehiculo WHERE \"Id\"=@id", conexion);
            sql.Parameters.AddWithValue("id", evento.Posicion.Id); Assert.Equal(1L, await sql.ExecuteScalarAsync(ct));
            Eventos.Add(evento); if (Fallar) throw new InvalidOperationException("Fallo de tablero simulado");
        }
    }
}
