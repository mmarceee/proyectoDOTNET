using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task Outbox_recibe_filtro_aunque_no_herede_Entity_y_rechaza_escrituras_ajenas()
    {
        using var scope = factory.Services.CreateScope();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        foreach (var operador in new[] { a, b })
        {
            await using var guardar = Contexto<EnviosDbContext>(scope, operador);
            guardar.OutboxMessages.Add(OutboxMessage.Crear(operador, "Prueba.v1", new { Valor = 1 }, DateTimeOffset.UtcNow));
            await guardar.SaveChangesAsync();
        }
        foreach (var operador in new Guid?[] { a, b, null })
        {
            await using var leer = Contexto<EnviosDbContext>(scope, operador);
            var mensajes = await leer.OutboxMessages.ToListAsync();
            if (operador is null) Assert.Empty(mensajes);
            else Assert.Equal(operador, Assert.Single(mensajes).OperadorId);
        }
        await using var comoA = Contexto<EnviosDbContext>(scope, a);
        comoA.OutboxMessages.Add(OutboxMessage.Crear(b, "Prueba.v1", new { Valor = 2 }, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<TenantMismatchException>(() => comoA.SaveChangesAsync());
    }

    private static T Contexto<T>(IServiceScope scope, Guid? operador) where T : ModuleDbContext
        => ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(operador, null));
    private static Ruta NuevaRuta(Guid operador) => Ruta.Crear(operador, new(2035, 1, 1),
        Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], DateTimeOffset.UtcNow);
    private static async Task<Ruta> GuardarRutaAsync(IServiceScope scope, Guid operador)
    {
        var ruta = NuevaRuta(operador);
        var envioId = ruta.Paradas[0].EnvioId;
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
