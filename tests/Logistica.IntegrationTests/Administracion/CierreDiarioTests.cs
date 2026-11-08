using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Application.Features.EjecutarCierreDiario;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Administracion;

// CU-70: el cierre diario del operador Demo, ejecutado a mano (en las pruebas el servicio periódico está apagado) con la
// hora del cierre en 00:00, así ya pasó cualquiera sea la hora de la corrida. Las reglas vigentes de Demo tienen 5 días
// de plazo de devolución.
public class CierreDiarioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid ZonaDemo = Guid.Parse("61111111-1111-1111-1111-111111111111");

    private WebApplicationFactory<Program> Api() =>
        factory.WithWebHostBuilder(b => b.UseSetting("CierreDiario:Hora", "00:00"));

    [Fact]
    public async Task Devuelve_los_reprogramados_vencidos_alerta_las_rutas_y_no_se_repite()
    {
        using var api = Api();
        var vencido = await ReprogramadoAsync(api, DateTimeOffset.UtcNow.AddDays(-6));
        var reciente = await ReprogramadoAsync(api, DateTimeOffset.UtcNow.AddHours(-1));
        var ruta = await RutaSinFinalizarAsync(api, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)));

        var cierre = await CerrarAsync(api);
        var repetido = await CerrarAsync(api);

        // Pasos 2, 3 y 6.
        Assert.NotNull(cierre);
        Assert.True(cierre.EnviosDevueltos >= 1);
        Assert.True(cierre.RutasSinFinalizar >= 1);
        // A1: el segundo cierre del mismo día no hace nada.
        Assert.Null(repetido);

        await using var scope = api.Services.CreateAsyncScope();
        await using var envios = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        var devuelto = await envios.Envios.AsNoTracking().Include(e => e.Eventos).SingleAsync(e => e.Id == vencido.Id);
        Assert.Equal(EstadoEnvio.EnDevolucion, devuelto.Estado);
        var t14 = devuelto.Eventos.Single(e => e.EstadoNuevo == EstadoEnvio.EnDevolucion);
        Assert.Equal((EstadoEnvio.Reprogramado, OrigenEvento.Sistema), (t14.EstadoAnterior!.Value, t14.Origen));
        Assert.Equal(EstadoEnvio.Reprogramado, (await envios.Envios.AsNoTracking().SingleAsync(e => e.Id == reciente.Id)).Estado);
        Assert.Single(await envios.Outbox.AsNoTracking().Where(m => m.Tipo == DevolucionIniciada.Tipo).ToListAsync(),
            m => JsonSerializer.Deserialize<DevolucionIniciada>(m.Contenido)!.EnvioId == vencido.Id);

        await using var planificacion = ActivatorUtilities.CreateInstance<PlanificacionDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        var alertas = (await planificacion.Outbox.AsNoTracking().Where(m => m.Tipo == RutasSinFinalizar.Tipo).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<RutasSinFinalizar>(m.Contenido)!).ToList();
        Assert.Contains(alertas, a => a.RutaIds.Contains(ruta));
        Assert.Single(alertas);

        await using var administracion = ActivatorUtilities.CreateInstance<AdministracionDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        Assert.Single(await administracion.CierresDiarios.AsNoTracking().ToListAsync());
    }

    // Paso 4, A1: un período sin envíos sin liquidar no genera liquidaciones ni falla.
    [Fact]
    public async Task Un_periodo_sin_envios_no_genera_liquidaciones()
    {
        using var api = Api();
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoTrabajo>().Fijar(DatosIniciales.OperadorDemoId);

        var generadas = await scope.ServiceProvider.GetRequiredService<IDepositoModuleApi>()
            .GenerarLiquidacionesDelPeriodoAsync(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31), default);

        Assert.Equal(0, generadas);
    }

    private static async Task<Modules.Administracion.Domain.CierresDiarios.CierreDiario?> CerrarAsync(
        WebApplicationFactory<Program> api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoTrabajo>().Fijar(DatosIniciales.OperadorDemoId);
        return await scope.ServiceProvider.GetRequiredService<EjecutarCierreDiarioHandler>().HandleAsync(default);
    }

    // Un envío de Demo llevado hasta Reprogramado por el camino real (T2, T3, T5 y el intento fallido de CU-18), sin
    // versión de reglas: usa las vigentes, como los envíos viejos.
    private static async Task<Envio> ReprogramadoAsync(WebApplicationFactory<Program> api, DateTimeOffset fallo)
    {
        api.CreateClient(); // arranca la API: migraciones y datos iniciales
        var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, $"ENV-C-{Guid.NewGuid():N}"[..20],
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, fallo.AddDays(-1));
        envio.DefinirPlanificacion(ZonaDemo, null, null);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, fallo.AddHours(-12));
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, fallo.AddHours(-3));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, fallo.AddHours(-2));
        envio.RegistrarIntentoFallido(new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false),
            null, null, new Ubicacion(-34.9m, -56.16m), new ReglasEntrega(false, false, maxIntentos: 3, TimeSpan.FromHours(24)),
            null, fallo);

        await using var scope = api.Services.CreateAsyncScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    // Una ruta Planificada de Demo de un día anterior, que nunca se despachó.
    private static async Task<Guid> RutaSinFinalizarAsync(WebApplicationFactory<Program> api, DateOnly fecha)
    {
        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, fecha, Guid.NewGuid(), Guid.NewGuid(),
            [new EnvioRuta(Guid.NewGuid(), "ENV-CU70-RUTA", null, null, null, false, [])], DateTimeOffset.UtcNow);
        await using var scope = api.Services.CreateAsyncScope();
        await using var db = ActivatorUtilities.CreateInstance<PlanificacionDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        db.Rutas.Add(ruta);
        await db.SaveChangesAsync();
        return ruta.Id;
    }
}
