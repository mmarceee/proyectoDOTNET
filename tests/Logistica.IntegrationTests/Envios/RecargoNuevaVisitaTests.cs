using System.Security.Claims;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

// RF 33, recargo de la nueva visita (decisiones-entrega-parcial.md, sección 5), contra la base real: la segunda visita de
// un envío de Envíos del Este (sus reglas iniciales cobran $150) registra un cargo, y la liquidación lo lee por contrato
// sólo con su operador (ADR-0002). El reenvío de la sincronización no repite la visita: lo cubre VisitaPorBultoTests.
public sealed class RecargoNuevaVisitaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid Este = DatosIniciales.OperadorEsteId;

    [Fact]
    public async Task La_nueva_visita_registra_un_recargo_que_la_liquidacion_lee_sin_recalcularlo()
    {
        var (envioId, bultoId) = await EnvioEnTransitoAsync();
        var ausente = await MotivoAsync("AUSENTE");
        var desde = DateTimeOffset.UtcNow.AddDays(-1);

        using (var scope = Scope(Este))
        {
            var envios = scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>();
            var primera = await envios.RegistrarVisitaAsync(Visita(envioId, bultoId, ausente), CancellationToken.None);
            Assert.Equal(ResultadoRegistro.Aplicado, primera.Resultado);
        }

        Assert.Empty(await RecargosAsync(Este, desde));

        await VolverATransitoAsync(envioId);
        using (var scope = Scope(Este))
        {
            var envios = scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>();
            var segunda = await envios.RegistrarVisitaAsync(Visita(envioId, bultoId, ausente), CancellationToken.None);
            Assert.Equal(ResultadoRegistro.Aplicado, segunda.Resultado);
        }

        var recargo = Assert.Single(await RecargosAsync(Este, desde));
        Assert.Equal((envioId, 150m), (recargo.EnvioId, recargo.Monto));
        Assert.Empty(await RecargosAsync(DatosIniciales.OperadorDemoId, desde));
    }

    private static VisitaARegistrar Visita(Guid envioId, Guid bultoId, Guid motivoId) =>
        new(envioId, DateTimeOffset.UtcNow.AddMinutes(-5), -34.9m, -56.16m, [new(bultoId, motivoId)], null, null, null, null,
            null, null);

    private async Task<IReadOnlyList<RecargoLiquidable>> RecargosAsync(Guid operadorId, DateTimeOffset desde)
    {
        using var scope = Scope(operadorId);
        return await scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>()
            .ListarRecargosAsync(DatosIniciales.ComercioDemoId, desde, DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);
    }

    // Un envío de un bulto de Envíos del Este, en tránsito. Sin versión de reglas guardada usa la vigente del operador.
    private async Task<(Guid EnvioId, Guid BultoId)> EnvioEnTransitoAsync()
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        var ahora = DateTimeOffset.UtcNow.AddHours(-2);
        var envio = Envio.Crear(Este, DatosIniciales.ComercioDemoId, $"ENV-R-{Guid.NewGuid():N}".ToUpperInvariant(),
            new Destinatario("Ana Pérez", "099123456"), new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "15000"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, ahora);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);

        await using var db = Contexto<EnviosDbContext>(Este);
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return (envio.Id, envio.Bultos[0].Id);
    }

    // La revisita (RF 34) todavía no tiene operación propia: el envío reprogramado vuelve a ruta y sale.
    private async Task VolverATransitoAsync(Guid envioId)
    {
        await using var db = Contexto<EnviosDbContext>(Este);
        var envio = await db.Envios.SingleAsync(e => e.Id == envioId);
        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, DateTimeOffset.UtcNow.AddMinutes(-4));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, DateTimeOffset.UtcNow.AddMinutes(-3));
        await db.SaveChangesAsync();
    }

    // Un motivo del catálogo de la versión de reglas vigente de Envíos del Este (datos iniciales).
    private async Task<Guid> MotivoAsync(string codigo)
    {
        await using var db = Contexto<AdministracionDbContext>(Este);
        var ahora = DateTimeOffset.UtcNow;
        var vigente = await db.VersionesReglas
            .Where(v => v.VigenteDesde <= ahora && (v.VigenteHasta == null || v.VigenteHasta > ahora))
            .OrderByDescending(v => v.VigenteDesde).Select(v => v.Id).FirstAsync();
        return await db.MotivosNoEntrega.Where(m => m.VersionReglasId == vigente && m.Codigo == codigo)
            .Select(m => m.Id).SingleAsync();
    }

    // Un ámbito con el operador como claim de la sesión, como personal del operador (ADR-0002, sección 2.5).
    private IServiceScope Scope(Guid operadorId)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("operador_id", operadorId.ToString())], "Prueba")),
        };
        return scope;
    }

    private T Contexto<T>(Guid operadorId) where T : Logistica.BuildingBlocks.Infrastructure.Persistence.ModuleDbContext
    {
        var scope = factory.Services.CreateScope();
        return ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(operadorId, null));
    }
}
