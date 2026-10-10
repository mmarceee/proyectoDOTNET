using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.Modules.Deposito.Infrastructure.Persistence;
using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Tenancy;

// Prueba de aislamiento entre inquilinos (letra, §6.5; ADR-0002, sección 4): con dos operadores, y dos
// comercios dentro de uno, ninguna lectura ni escritura cruza de inquilino. Usa los DbContext reales de
// los módulos contra un PostgreSQL real; cada prueba crea sus propios operadores para no depender de las otras.
public sealed class AislamientoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    // ---- Lecturas: el filtro "Tenant" ----

    [Fact]
    public async Task Cada_operador_ve_solo_sus_envios_en_el_listado_y_en_el_detalle()
    {
        using var scope = factory.Services.CreateScope();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var deA = await GuardarAsync(scope, NuevoEnvio(a, Guid.NewGuid()));
        var deB = await GuardarAsync(scope, NuevoEnvio(b, Guid.NewGuid()));

        // Primero como A y después como B, con contextos distintos: si el inquilino quedara congelado
        // en el modelo (EF lo construye una sola vez), la consulta como B seguiría viendo lo de A.
        await VerificarQueSoloVeAsync(scope, a, propio: deA, ajeno: deB);
        await VerificarQueSoloVeAsync(scope, b, propio: deB, ajeno: deA);
    }

    [Fact]
    public async Task El_comercio_ve_solo_sus_envios_y_el_personal_del_operador_ve_los_de_todos_sus_comercios()
    {
        using var scope = factory.Services.CreateScope();
        var (operador, comercioX, comercioY) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var deX = await GuardarAsync(scope, NuevoEnvio(operador, comercioX));
        var deY = await GuardarAsync(scope, NuevoEnvio(operador, comercioY));

        await using (var comoX = Contexto<EnviosDbContext>(scope, operador, comercioX))
        {
            var listado = await new EnviosListadoReader(comoX).ConsultarAsync(new ConsultarEnviosQuery(), CancellationToken.None);
            Assert.Equal(deX.Numero, Assert.Single(listado.Envios).Numero);
            Assert.Null(await new EnvioDetalleReader(comoX).ConsultarAsync(new(deY.Numero), CancellationToken.None));
        }

        await using (var personal = Contexto<EnviosDbContext>(scope, operador))
        {
            var listado = await new EnviosListadoReader(personal).ConsultarAsync(new ConsultarEnviosQuery(), CancellationToken.None);
            Assert.Equal(2, listado.TotalRegistros);
        }
    }

    [Fact]
    public async Task Sin_operador_en_la_sesion_no_se_ve_nada_en_ningun_modulo()
    {
        using var scope = factory.Services.CreateScope();
        var operador = Guid.NewGuid();
        await GuardarAsync(scope, NuevoEnvio(operador, Guid.NewGuid()));
        await GuardarRecepcionAsync(scope, operador, Guid.NewGuid());

        // Hay filas en las tres tablas (la relación comercial la carga el seed), pero sin operador
        // el filtro falla cerrado. Con un comercio y sin operador, tampoco.
        foreach (var comercio in new Guid?[] { null, Guid.NewGuid() })
        {
            await using var envios = Contexto<EnviosDbContext>(scope, null, comercio);
            await using var deposito = Contexto<DepositoDbContext>(scope, null, comercio);
            await using var administracion = Contexto<AdministracionDbContext>(scope, null, comercio);

            Assert.False(await envios.Envios.AnyAsync());
            Assert.False(await deposito.Recepciones.AnyAsync());
            Assert.False(await administracion.RelacionesComerciales.AnyAsync());
        }
    }

    [Fact]
    public async Task Cada_operador_ve_solo_sus_recepciones_en_deposito()
    {
        using var scope = factory.Services.CreateScope();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var recepcionA = await GuardarRecepcionAsync(scope, a, Guid.NewGuid());
        var recepcionB = await GuardarRecepcionAsync(scope, b, Guid.NewGuid());

        await using var comoA = Contexto<DepositoDbContext>(scope, a);
        var repositorio = new RecepcionRepository(comoA);

        Assert.Equal(recepcionA.BultoId, Assert.Single(await repositorio.BultosRecibidosAsync(recepcionA.EnvioId, CancellationToken.None)));
        Assert.Empty(await repositorio.BultosRecibidosAsync(recepcionB.EnvioId, CancellationToken.None));
    }

    [Fact]
    public async Task Cada_operador_ve_solo_sus_relaciones_comerciales()
    {
        using var scope = factory.Services.CreateScope();

        await using var comoDemo = Contexto<AdministracionDbContext>(scope, DatosIniciales.OperadorDemoId);
        await using var comoOtro = Contexto<AdministracionDbContext>(scope, Guid.NewGuid());

        Assert.Contains(await comoDemo.RelacionesComerciales.ToListAsync(), r => r.ComercioId == DatosIniciales.ComercioDemoId);
        Assert.Empty(await comoOtro.RelacionesComerciales.ToListAsync());
    }

    // ---- Escrituras: TenantSaveChangesInterceptor ----

    [Fact]
    public async Task No_se_puede_guardar_un_envio_de_otro_operador()
    {
        using var scope = factory.Services.CreateScope();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var deB = NuevoEnvio(b, Guid.NewGuid());

        await using (var comoA = Contexto<EnviosDbContext>(scope, a))
        {
            comoA.Envios.Add(deB);
            await Assert.ThrowsAsync<TenantMismatchException>(() => comoA.SaveChangesAsync());
        }

        await AssertNoExisteAsync(scope, deB);
    }

    [Fact]
    public async Task Sin_operador_en_la_sesion_no_se_puede_guardar()
    {
        using var scope = factory.Services.CreateScope();
        var envio = NuevoEnvio(Guid.NewGuid(), Guid.NewGuid());

        await using (var sinOperador = Contexto<EnviosDbContext>(scope, null))
        {
            sinOperador.Envios.Add(envio);
            await Assert.ThrowsAsync<TenantMismatchException>(() => sinOperador.SaveChangesAsync());
        }

        await AssertNoExisteAsync(scope, envio);
    }

    [Fact]
    public async Task Un_comercio_no_puede_guardar_un_envio_de_otro_comercio()
    {
        using var scope = factory.Services.CreateScope();
        var (operador, comercioX, comercioY) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var deY = NuevoEnvio(operador, comercioY);

        await using (var comoX = Contexto<EnviosDbContext>(scope, operador, comercioX))
        {
            comoX.Envios.Add(deY);
            await Assert.ThrowsAsync<TenantMismatchException>(() => comoX.SaveChangesAsync());
        }

        await AssertNoExisteAsync(scope, deY);
    }

    [Fact]
    public async Task No_se_puede_cambiar_el_operador_de_un_envio_existente()
    {
        using var scope = factory.Services.CreateScope();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var guardado = await GuardarAsync(scope, NuevoEnvio(a, Guid.NewGuid()));

        await using (var comoA = Contexto<EnviosDbContext>(scope, a))
        {
            var envio = await comoA.Envios.SingleAsync(e => e.Id == guardado.Id);

            // El dominio no deja cambiarlo (setter privado); el change tracker sí, y eso es lo que se prueba.
            comoA.Entry(envio).Property(e => e.OperadorId).CurrentValue = b;

            await Assert.ThrowsAsync<TenantMismatchException>(() => comoA.SaveChangesAsync());
        }

        await using var otraVezComoA = Contexto<EnviosDbContext>(scope, a);
        Assert.True(await otraVezComoA.Envios.AnyAsync(e => e.Id == guardado.Id));
    }

    [Fact]
    public async Task Un_alta_sin_operador_ni_comercio_los_toma_de_la_sesion()
    {
        using var scope = factory.Services.CreateScope();
        var (operador, comercio) = (Guid.NewGuid(), Guid.NewGuid());
        var envio = NuevoEnvio(Guid.Empty, Guid.Empty);

        await using (var comoComercio = Contexto<EnviosDbContext>(scope, operador, comercio))
        {
            comoComercio.Envios.Add(envio);
            await comoComercio.SaveChangesAsync();
        }

        // También a los bultos, que entraron por la navegación del envío.
        Assert.Equal((operador, comercio), (envio.OperadorId, envio.ComercioId));
        Assert.All(envio.Bultos, b => Assert.Equal((operador, comercio), (b.OperadorId, b.ComercioId)));

        await using var leer = Contexto<EnviosDbContext>(scope, operador, comercio);
        var detalle = await new EnvioDetalleReader(leer).ConsultarAsync(new(envio.Numero), CancellationToken.None);
        Assert.Equal(comercio, detalle?.ComercioId);
    }

    // ---- Estructural: que el filtro esté aplicado ----

    [Fact]
    public void En_cada_DbContext_de_los_modulos_toda_entidad_de_un_operador_tiene_el_filtro_Tenant()
    {
        using var scope = factory.Services.CreateScope();

        // Los DbContext reales de todos los módulos, tal como los registra la API.
        var entidades = scope.ServiceProvider.GetServices<ModuleDbContext>()
            .SelectMany(db => db.Model.GetEntityTypes())
            .Where(t => typeof(IOperadorOwned).IsAssignableFrom(t.ClrType))
            .ToList();
        var sinFiltro = entidades
            .Where(t => t.FindDeclaredQueryFilter(ModuleDbContext.FiltroTenant) is null)
            .Select(t => t.ClrType.Name);

        // Detecta, por ejemplo, un OnModelCreating que no llama a la clase base.
        Assert.Empty(sinFiltro);

        // Y que la prueba no pase por vacía: hay entidades de los tres módulos con datos.
        var nombres = entidades.Select(t => t.ClrType.Name).ToList();
        Assert.Contains(nameof(Envio), nombres);
        Assert.Contains(nameof(RecepcionDeposito), nombres);
        Assert.Contains("RelacionComercial", nombres);
    }

    // ---- Ayudas ----

    // Un DbContext real del módulo con el inquilino que la prueba quiere probar; el resto de sus
    // dependencias (opciones, unidad de trabajo) sale de la API.
    private static T Contexto<T>(IServiceScope scope, Guid? operadorId, Guid? comercioId = null)
        where T : ModuleDbContext
    {
        return ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(operadorId, comercioId));
    }

    private static Envio NuevoEnvio(Guid operadorId, Guid comercioId)
    {
        return Envio.Crear(operadorId, comercioId, $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana", "099123456"),
            new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 100)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow);
    }

    // Lo guarda como personal de su operador, que puede escribir envíos de cualquiera de sus comercios.
    private static async Task<Envio> GuardarAsync(IServiceScope scope, Envio envio)
    {
        await using var db = Contexto<EnviosDbContext>(scope, envio.OperadorId);
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    private static async Task<RecepcionDeposito> GuardarRecepcionAsync(IServiceScope scope, Guid operadorId, Guid envioId)
    {
        var medidas = new Medidas(1, 10, 10, 10);
        var recepcion = RecepcionDeposito.Registrar(operadorId, envioId, Guid.NewGuid(), medidas, medidas, DateTimeOffset.UtcNow);

        await using var db = Contexto<DepositoDbContext>(scope, operadorId);
        db.Recepciones.Add(recepcion);
        await db.SaveChangesAsync();
        return recepcion;
    }

    private static async Task VerificarQueSoloVeAsync(IServiceScope scope, Guid operador, Envio propio, Envio ajeno)
    {
        await using var db = Contexto<EnviosDbContext>(scope, operador);
        var listado = await new EnviosListadoReader(db).ConsultarAsync(new ConsultarEnviosQuery(), CancellationToken.None);
        var detalle = new EnvioDetalleReader(db);

        Assert.Equal(propio.Numero, Assert.Single(listado.Envios).Numero);
        Assert.NotNull(await detalle.ConsultarAsync(new(propio.Numero), CancellationToken.None));
        Assert.Null(await detalle.ConsultarAsync(new(ajeno.Numero), CancellationToken.None));
    }

    // Lo busca como su propio operador: si no lo ve él, no quedó guardado.
    private static async Task AssertNoExisteAsync(IServiceScope scope, Envio envio)
    {
        await using var db = Contexto<EnviosDbContext>(scope, envio.OperadorId);
        Assert.False(await db.Envios.AnyAsync(e => e.Id == envio.Id));
    }
}
