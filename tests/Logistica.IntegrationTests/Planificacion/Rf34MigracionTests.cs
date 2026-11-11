using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.Modules.Deposito.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Cargas;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

public sealed class Rf34MigracionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Migrar_conserva_hechos_y_bloquea_pertenencia_o_ubicacion_ambiguas()
    {
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        await using var envios = Contexto<EnviosDbContext>(scope);
        await using var plan = Contexto<PlanificacionDbContext>(scope);
        await using var deposito = Contexto<DepositoDbContext>(scope);
        await using var ejecucion = Contexto<EjecucionDbContext>(scope);
        var ahora = DateTimeOffset.UtcNow.AddDays(-2);
        var recibido = Crear(ahora);
        var asignado = Crear(ahora);
        var ambiguo = Crear(ahora);
        var sinManifiesto = Crear(ahora);
        foreach (var e in new[] { recibido, asignado, ambiguo, sinManifiesto })
            e.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        foreach (var e in new[] { asignado, ambiguo, sinManifiesto })
            e.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        asignado.CargarBultos([asignado.Bultos[0].Id], null, ahora);
        envios.Envios.AddRange(recibido, asignado, ambiguo, sinManifiesto);
        await envios.SaveChangesAsync();
        foreach (var b in recibido.Bultos)
            deposito.Recepciones.Add(RecepcionDeposito.Registrar(recibido.OperadorId, recibido.Id, b.Id,
                new Medidas(1, 10, 10, 10), new Medidas(1, 10, 10, 10), ahora));
        await deposito.SaveChangesAsync();

        Ruta GuardarRuta(Envio e, int dia, bool finalizada, bool validar)
        {
            var datos = new EnvioRuta(e.Id, e.Numero, null, null, null, false,
                e.Bultos.Select(b => new CargaBulto(e.Id, b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToArray());
            var ruta = Ruta.Crear(e.OperadorId, new DateOnly(2037, 1, dia), DatosIniciales.RepartidorDemoId,
                Guid.NewGuid(), [datos], ahora);
            ruta.Despachar(ahora);
            if (finalizada) ruta.Finalizar();
            plan.Rutas.Add(ruta);
            if (validar) plan.Validaciones.Add(ValidacionRuta.Registrar(ruta, OperacionValidacionRuta.Creacion,
                new VehiculoRuta(ruta.VehiculoId, 100, 100, 100, 100, 100), Guid.NewGuid(), 100, [datos], null, ahora));
            return ruta;
        }
        var inequívoca = GuardarRuta(asignado, 1, false, true);
        GuardarRuta(ambiguo, 2, false, true);
        var antigua = GuardarRuta(ambiguo, 3, true, true);
        GuardarRuta(sinManifiesto, 4, false, false);
        await plan.SaveChangesAsync();
        ejecucion.EscaneosCarga.Add(EscaneoCarga.Crear(asignado.OperadorId, Guid.NewGuid(), inequívoca.Id,
            asignado.Bultos[0].Id, asignado.Bultos[0].Codigo, true, ahora));
        var rendicion = Rendicion.Iniciar(ambiguo.OperadorId, antigua.Id, antigua.RepartidorId, Guid.NewGuid(), ahora,
            null, ambiguo.Bultos.Select(b => new DeclaracionRendicion(ambiguo.Id, b.Id, "NoCargado", false, null)).ToArray());
        rendicion.Confirmar(new HashSet<Guid>(), ahora);
        ejecucion.Rendiciones.Add(rendicion); await ejecucion.SaveChangesAsync();
        var eventosAntes = await envios.Envios.Where(e => e.Id == asignado.Id).SelectMany(e => e.Eventos).CountAsync();

        // Solamente esta base efímera de prueba vuelve al esquema anterior para ensayar una actualización real.
        var migradorPlan = plan.GetService<IMigrator>();
        var migradorEnvios = envios.GetService<IMigrator>();
        await migradorPlan.MigrateAsync(plan.Database.GetMigrations().Reverse().Skip(1).First());
        await migradorEnvios.MigrateAsync(envios.Database.GetMigrations().Reverse().Skip(1).First());
        await migradorEnvios.MigrateAsync();
        await migradorPlan.MigrateAsync();
        envios.ChangeTracker.Clear(); plan.ChangeTracker.Clear();

        var recepcion = await envios.Envios.SingleAsync(e => e.Id == recibido.Id);
        Assert.All(recepcion.Bultos, b => Assert.Equal(UbicacionCustodia.Deposito, b.Custodia));
        var reconstruido = await envios.Envios.SingleAsync(e => e.Id == asignado.Id);
        Assert.Equal(UbicacionCustodia.Repartidor, reconstruido.Bultos.Single(b => b.Id == asignado.Bultos[0].Id).Custodia);
        Assert.Equal(inequívoca.Id, reconstruido.Bultos.Single(b => b.Id == asignado.Bultos[0].Id).CustodiaRutaId);
        Assert.Equal(2, await plan.ReservasBulto.CountAsync(r => r.EnvioId == asignado.Id && r.CerradaEn == null));
        Assert.Equal(eventosAntes, await envios.Envios.Where(e => e.Id == asignado.Id).SelectMany(e => e.Eventos).CountAsync());
        foreach (var e in new[] { ambiguo, sinManifiesto })
        {
            Assert.False(await plan.ReservasBulto.AnyAsync(r => r.EnvioId == e.Id));
            Assert.All((await envios.Envios.SingleAsync(x => x.Id == e.Id)).Bultos,
                b => Assert.Equal(UbicacionCustodia.Desconocida, b.Custodia));
            Assert.Equal(e.Bultos.Count, await envios.Set<MovimientoCustodia>().CountAsync(m => m.EnvioId == e.Id));
        }
        Assert.False(envios.Database.HasPendingModelChanges());
        Assert.False(plan.Database.HasPendingModelChanges());
    }

    private static Envio Crear(DateTimeOffset ahora) => Envio.Crear(DatosIniciales.OperadorDemoId,
        DatosIniciales.ComercioDemoId, $"MIG34-{Guid.NewGuid():N}", new Destinatario("Ana", "099123456"),
        new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
        [new DatosBulto(1, 10, 10, 10, 100), new DatosBulto(1, 10, 10, 10, 100)], OrigenEvento.PortalComercio, null, ahora);
    private static T Contexto<T>(IServiceScope scope) where T : Logistica.BuildingBlocks.Infrastructure.Persistence.ModuleDbContext
        => ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
}
