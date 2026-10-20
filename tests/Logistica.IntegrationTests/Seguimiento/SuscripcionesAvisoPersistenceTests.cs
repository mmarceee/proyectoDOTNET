using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Application;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

public sealed class SuscripcionesAvisoPersistenceTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Guarda_y_recupera_eventos_y_actualiza_el_secreto_y_la_activacion()
    {
        using var scope = factory.Services.CreateScope();
        var operador = Guid.NewGuid();
        var comercio = Guid.NewGuid();
        var suscripcion = Nueva(operador, comercio);
        await GuardarAsync(scope, suscripcion);

        await using (var db = Contexto<SeguimientoDbContext>(scope, operador, comercio))
        {
            var repo = new SuscripcionesAvisoRepository(db);
            var recuperada = (await repo.ObtenerAsync(suscripcion.Id, default))!;
            Assert.Equal(suscripcion.RelacionComercialId, recuperada.RelacionComercialId);
            Assert.Equal(suscripcion.UrlDestino, recuperada.UrlDestino);
            Assert.Equal(suscripcion.TiposEvento, recuperada.TiposEvento);
            Assert.Equal("secreto-cifrado", recuperada.SecretoFirma);
            Assert.True(recuperada.Activa);
            recuperada.RegenerarSecreto("nuevo-secreto-cifrado");
            recuperada.Desactivar();
            await repo.GuardarAsync(default);
        }

        await using var lectura = Contexto<SeguimientoDbContext>(scope, operador, comercio);
        var resultado = Assert.Single(await new SuscripcionesAvisoRepository(lectura).ListarAsync(default));
        Assert.False(resultado.Activa);
        Assert.Equal("nuevo-secreto-cifrado", resultado.SecretoFirma);
        Assert.Equal(suscripcion.TiposEvento, resultado.TiposEvento);
    }

    [Fact]
    public async Task Lista_y_detalle_aislan_operador_y_comercio_y_sin_operador_no_devuelven_datos()
    {
        using var scope = factory.Services.CreateScope();
        var operador = Guid.NewGuid();
        var comercio = Guid.NewGuid();
        var propia = Nueva(operador, comercio);
        var otroComercio = Nueva(operador, Guid.NewGuid());
        var otroOperador = Nueva(Guid.NewGuid(), comercio);
        foreach (var suscripcion in new[] { propia, otroComercio, otroOperador })
            await GuardarAsync(scope, suscripcion);

        await using (var db = Contexto<SeguimientoDbContext>(scope, operador, comercio))
        {
            var repo = new SuscripcionesAvisoRepository(db);
            Assert.Equal(propia.Id, Assert.Single(await repo.ListarAsync(default)).Id);
            Assert.Null(await repo.ObtenerAsync(otroComercio.Id, default));
            Assert.Null(await repo.ObtenerAsync(otroOperador.Id, default));
        }

        await using (var db = Contexto<SeguimientoDbContext>(scope, otroOperador.OperadorId, comercio))
            Assert.Equal(otroOperador.Id, Assert.Single(await new SuscripcionesAvisoRepository(db).ListarAsync(default)).Id);

        foreach (var comercioActual in new Guid?[] { null, comercio })
        {
            await using var db = Contexto<SeguimientoDbContext>(scope, null, comercioActual);
            var repo = new SuscripcionesAvisoRepository(db);
            Assert.Empty(await repo.ListarAsync(default));
            Assert.Null(await repo.ObtenerAsync(propia.Id, default));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rechaza_escrituras_de_otro_operador_o_comercio(bool otroOperador)
    {
        using var scope = factory.Services.CreateScope();
        var operador = Guid.NewGuid();
        var comercio = Guid.NewGuid();
        var ajena = Nueva(otroOperador ? Guid.NewGuid() : operador,
            otroOperador ? comercio : Guid.NewGuid());
        await using var db = Contexto<SeguimientoDbContext>(scope, operador, comercio);
        var repo = new SuscripcionesAvisoRepository(db);
        repo.Agregar(ajena);
        await Assert.ThrowsAsync<TenantMismatchException>(() => repo.GuardarAsync(default));

        await using var verificacion = Contexto<SeguimientoDbContext>(scope, ajena.OperadorId, ajena.ComercioId);
        Assert.Null(await new SuscripcionesAvisoRepository(verificacion).ObtenerAsync(ajena.Id, default));
    }

    [Fact]
    public async Task Administracion_resuelve_solo_la_relacion_de_la_sesion_actual()
    {
        using var scope = factory.Services.CreateScope();
        var comercio = DatosIniciales.ComercioDemoId;
        var demo = await RelacionActualAsync(scope, DatosIniciales.OperadorDemoId, comercio);
        var este = await RelacionActualAsync(scope, DatosIniciales.OperadorEsteId, comercio);
        Assert.NotNull(demo);
        Assert.NotNull(este);
        Assert.NotEqual(demo, este);

        await using (var db = Contexto<AdministracionDbContext>(scope, DatosIniciales.OperadorDemoId, comercio))
            Assert.Equal((await db.RelacionesComerciales.SingleAsync()).Id, demo);

        Assert.Null(await RelacionActualAsync(scope, Guid.NewGuid(), comercio));
        Assert.Null(await RelacionActualAsync(scope, DatosIniciales.OperadorDemoId, Guid.NewGuid()));
        Assert.Null(await RelacionActualAsync(scope, null, comercio));
        Assert.Null(await RelacionActualAsync(scope, DatosIniciales.OperadorDemoId, null));
    }

    private static async Task<Guid?> RelacionActualAsync(IServiceScope scope, Guid? operador, Guid? comercio)
    {
        var tenant = new InquilinoFijo(operador, comercio);
        await using var db = Contexto<AdministracionDbContext>(scope, operador, comercio);
        IAdministracionModuleApi api = ActivatorUtilities.CreateInstance<AdministracionModuleApi>(scope.ServiceProvider,
            tenant, new ComerciosRepository(db, tenant));
        return await api.ObtenerRelacionComercialActualAsync(default);
    }

    private static T Contexto<T>(IServiceScope scope, Guid? operador, Guid? comercio) where T : ModuleDbContext =>
        ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(operador, comercio));

    private static SuscripcionAviso Nueva(Guid operador, Guid comercio) =>
        SuscripcionAviso.Crear(operador, comercio, Guid.NewGuid(), "https://comercio.example/avisos",
            [TipoEventoAviso.CambioDeEstado, TipoEventoAviso.EntregaFallida, TipoEventoAviso.Devolucion], "secreto-cifrado");

    private static async Task GuardarAsync(IServiceScope scope, SuscripcionAviso suscripcion)
    {
        await using var db = Contexto<SeguimientoDbContext>(scope, suscripcion.OperadorId, suscripcion.ComercioId);
        var repo = new SuscripcionesAvisoRepository(db);
        repo.Agregar(suscripcion);
        await repo.GuardarAsync(default);
    }
}
