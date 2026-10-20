using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;
using Logistica.Modules.Envios.Contracts;

namespace Logistica.IntegrationTests.Planificacion;

public class AislamientoPlanificacionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Rutas_paradas_y_evidencia_se_aislan_por_operador_y_sin_tenant_no_se_leen()
    {
        using var scope = factory.Services.CreateScope();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var propia = await GuardarRutaAsync(scope, a);
        var ajena = await GuardarRutaAsync(scope, b);
        foreach (var operador in new Guid?[] { a, b, null })
        {
            await using var db = Contexto<PlanificacionDbContext>(scope, operador);
            var rutas = await db.Rutas.Include(r => r.Paradas).ToListAsync();
            var pruebas = await db.Validaciones.Include(v => v.Bultos).ToListAsync();
            if (operador is null)
            {
                Assert.Empty(rutas); Assert.Empty(pruebas);
                Assert.Empty(await db.Paradas.ToListAsync());
                Assert.Empty(await db.Set<BultoValidacionRuta>().ToListAsync());
            }
            else
            {
                Assert.Equal(operador == a ? propia.Id : ajena.Id, Assert.Single(rutas).Id);
                Assert.Equal(operador, Assert.Single(rutas[0].Paradas).OperadorId);
                Assert.Equal(rutas[0].Id, Assert.Single(pruebas).RutaId);
                Assert.Equal(operador, Assert.Single(pruebas[0].Bultos).OperadorId);
                Assert.Equal(operador, Assert.Single(await db.Paradas.ToListAsync()).OperadorId);
                Assert.Equal(operador, Assert.Single(await db.Set<BultoValidacionRuta>().ToListAsync()).OperadorId);
            }
        }
    }

    [Fact]
    public async Task Un_hijo_de_otro_operador_impide_guardar_toda_la_ruta()
    {
        using var scope = factory.Services.CreateScope();
        var a = Guid.NewGuid();
        var ruta = NuevaRuta(a);
        await using (var db = Contexto<PlanificacionDbContext>(scope, a))
        {
            db.Rutas.Add(ruta);
            db.Entry(ruta.Paradas[0]).Property(p => p.OperadorId).CurrentValue = Guid.NewGuid();
            await Assert.ThrowsAsync<TenantMismatchException>(() => db.SaveChangesAsync());
        }
        await using var leer = Contexto<PlanificacionDbContext>(scope, a);
        Assert.False(await leer.Rutas.AnyAsync(r => r.Id == ruta.Id));
    }

    [Fact]
    public async Task Listado_combina_fecha_y_estado_sin_perder_aislamiento()
    {
        using var scope = factory.Services.CreateScope();
        var operador = Guid.NewGuid();
        var planificada = NuevaRuta(operador);
        var despachada = NuevaRuta(operador); despachada.Despachar(DateTimeOffset.UtcNow);
        var enCurso = NuevaRuta(operador); enCurso.Despachar(DateTimeOffset.UtcNow); enCurso.Iniciar();
        var finalizada = NuevaRuta(operador); finalizada.Despachar(DateTimeOffset.UtcNow); finalizada.Finalizar();
        var otroDia = NuevaRuta(operador);
        otroDia.ModificarPlanificacion(planificada.Fecha.AddDays(1), otroDia.RepartidorId, otroDia.VehiculoId);
        await using var db = Contexto<PlanificacionDbContext>(scope, operador);
        db.Rutas.AddRange(planificada, despachada, enCurso, finalizada, otroDia);
        await db.SaveChangesAsync();
        await GuardarRutaAsync(scope, Guid.NewGuid());
        var handler = new ConsultarRutasHandler(new RutaRepository(db), scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>(),
            scope.ServiceProvider.GetRequiredService<Logistica.Modules.Administracion.Contracts.IAdministracionModuleApi>());
        Assert.Equal(5, (await handler.ListarAsync(null, null, default)).Count);
        Assert.Equal(2, (await handler.ListarAsync(null, "Planificada", default)).Count);
        Assert.Equal(planificada.Id, Assert.Single(await handler.ListarAsync(planificada.Fecha, "Planificada", default)).Id);
        Assert.Equal(despachada.Id, Assert.Single(await handler.ListarAsync(null, "Despachada", default)).Id);
        Assert.Equal(enCurso.Id, Assert.Single(await handler.ListarAsync(null, "EnCurso", default)).Id);
        Assert.Equal(finalizada.Id, Assert.Single(await handler.ListarAsync(null, "Finalizada", default)).Id);
        Assert.Empty(await handler.ListarAsync(otroDia.Fecha, "Despachada", default));
        await Assert.ThrowsAsync<DomainException>(() => handler.ListarAsync(null, "999", default));
    }

    // Las pruebas del outbox están en BuildingBlocks/OutboxTests: el outbox no lleva filtro "Tenant" (ADR-0002, sección 2.4).

    private static T Contexto<T>(IServiceScope scope, Guid? operador) where T : ModuleDbContext
        => ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(operador, null));
    private static Ruta NuevaRuta(Guid operador) => Ruta.Crear(operador, new(2035, 1, 1),
        Guid.NewGuid(), Guid.NewGuid(), [new EnvioRuta(Guid.NewGuid(), "ENV-TEST", null, null, null, false, [])], DateTimeOffset.UtcNow);
    private static async Task<Ruta> GuardarRutaAsync(IServiceScope scope, Guid operador)
    {
        var ruta = NuevaRuta(operador);
        var envioId = ruta.Paradas[0].Envios[0].EnvioId;
        var envio = new EnvioRuta(envioId, "ENV-PRUEBA", null, null, null, false,
            [new CargaBulto(envioId, Guid.NewGuid(), 1, 10, 10, 10)]);
        var evidencia = ValidacionRuta.Registrar(ruta, OperacionValidacionRuta.Creacion,
            new(ruta.VehiculoId, 100, 1, 100, 100, 100), Guid.NewGuid(), 20, [envio], null, DateTimeOffset.UtcNow);
        await using var db = Contexto<PlanificacionDbContext>(scope, operador);
        db.Rutas.Add(ruta); db.Validaciones.Add(evidencia);
        await db.SaveChangesAsync();
        return ruta;
    }
}
