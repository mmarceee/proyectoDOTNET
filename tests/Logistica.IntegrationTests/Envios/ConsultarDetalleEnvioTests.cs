using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

public sealed class ConsultarDetalleEnvioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Devuelve_datos_bultos_y_eventos_en_orden_sin_seguimiento()
    {
        using var scope = factory.Services.CreateScope();
        var envio = await CrearEnvioAsync(scope);
        await using var db = Contexto(scope, new Tenant(envio.OperadorId, envio.ComercioId));
        var handler = new ConsultarDetalleEnvioHandler(new EnvioDetalleReader(db),
            scope.ServiceProvider.GetRequiredService<Logistica.Modules.Deposito.Contracts.IDepositoModuleApi>(),
            new Tenant(envio.OperadorId, envio.ComercioId));

        var detalle = await handler.HandleAsync(new($" {envio.Numero} "), CancellationToken.None);

        Assert.NotNull(detalle);
        Assert.Equal(envio.Numero, detalle.Numero);
        Assert.Equal(envio.ComercioId, detalle.ComercioId);
        Assert.Equal("EnDeposito", detalle.Estado);
        Assert.Equal(envio.CreadoEn, detalle.CreadoEn);
        Assert.Equal("Ana Pérez", detalle.Destinatario.Nombre);
        Assert.Equal("099123456", detalle.Destinatario.Telefono);
        Assert.Null(detalle.Destinatario.Email);
        Assert.Null(detalle.Destinatario.Documento);
        Assert.Equal("Av. Italia", detalle.Direccion.Calle);
        Assert.Equal("1234", detalle.Direccion.Numero);
        Assert.Equal("Montevideo", detalle.Direccion.Localidad);
        Assert.Equal("Montevideo", detalle.Direccion.Departamento);
        Assert.Equal("11300", detalle.Direccion.CodigoPostal);
        Assert.Null(detalle.Direccion.Referencia);
        Assert.Equal(250m, detalle.MontoTarifa);
        Assert.Null(detalle.VersionTarifario);
        Assert.Empty(detalle.Intentos);
        Assert.Empty(detalle.Incidencias);
        Assert.Null(detalle.Devolucion);
        Assert.Collection(detalle.Bultos,
            b =>
            {
                Assert.Equal($"{envio.Numero}-1", b.Codigo);
                Assert.Equal(2m, b.PesoKg);
                Assert.Equal(30m, b.LargoCm);
                Assert.Equal(20m, b.AnchoCm);
                Assert.Equal(10m, b.AltoCm);
                Assert.Equal(100m, b.MontoTarifa);
            },
            b =>
            {
                Assert.Equal($"{envio.Numero}-2", b.Codigo);
                Assert.Equal(150m, b.MontoTarifa);
            });
        Assert.Collection(detalle.Eventos,
            ev =>
            {
                Assert.Null(ev.EstadoAnterior);
                Assert.Equal("Admitido", ev.EstadoNuevo);
                Assert.Equal("PortalComercio", ev.Origen);
                Assert.Equal(envio.CreadoEn, ev.OcurridoEn);
                Assert.Null(ev.ResponsableId);
            },
            ev =>
            {
                Assert.Equal("Admitido", ev.EstadoAnterior);
                Assert.Equal("EnDeposito", ev.EstadoNuevo);
                Assert.Equal("Backoffice", ev.Origen);
                Assert.Equal(envio.CreadoEn.AddMinutes(1), ev.OcurridoEn);
                Assert.Equal(envio.Eventos[1].ResponsableId, ev.ResponsableId);
            });
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Personal_del_operador_puede_ver_envios_de_distintos_comercios()
    {
        using var scope = factory.Services.CreateScope();
        var operadorId = Guid.NewGuid();
        var primero = await CrearEnvioAsync(scope, operadorId);
        var segundo = await CrearEnvioAsync(scope, operadorId);
        await using var db = Contexto(scope, new Tenant(operadorId, null));
        var reader = new EnvioDetalleReader(db);

        Assert.NotNull(await reader.ConsultarAsync(new(primero.Numero), CancellationToken.None));
        Assert.NotNull(await reader.ConsultarAsync(new(segundo.Numero), CancellationToken.None));
    }

    [Theory]
    [InlineData("otro-operador")]
    [InlineData("otro-comercio")]
    [InlineData("sin-operador")]
    [InlineData("inexistente")]
    public async Task Devuelve_null_si_el_envio_no_es_visible_o_no_existe(string caso)
    {
        using var scope = factory.Services.CreateScope();
        var envio = await CrearEnvioAsync(scope);
        var tenant = caso switch
        {
            "otro-operador" => new Tenant(Guid.NewGuid(), envio.ComercioId),
            "otro-comercio" => new Tenant(envio.OperadorId, Guid.NewGuid()),
            "sin-operador" => new Tenant(null, envio.ComercioId),
            _ => new Tenant(envio.OperadorId, envio.ComercioId),
        };
        await using var db = Contexto(scope, tenant);
        var reader = new EnvioDetalleReader(db);
        var numero = caso == "inexistente" ? $"INEXISTENTE-{Guid.NewGuid():N}" : envio.Numero;

        Assert.Null(await reader.ConsultarAsync(new(numero), CancellationToken.None));
    }

    // El filtro "Tenant" lee el inquilino del DbContext: cada prueba arma el suyo con el inquilino
    // que quiere probar, y el resto de las dependencias (opciones, unidad de trabajo) sale de la API.
    private static EnviosDbContext Contexto(IServiceScope scope, ICurrentTenant tenant)
    {
        return ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider, tenant);
    }

    // Lo guarda como personal del operador, que puede escribir envíos de cualquiera de sus comercios.
    private static async Task<Envio> CrearEnvioAsync(IServiceScope scope, Guid? operadorId = null)
    {
        var ahora = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var operador = operadorId ?? Guid.NewGuid();
        await using var db = Contexto(scope, new Tenant(operador, null));
        var envio = Envio.Crear(
            operador, Guid.NewGuid(), $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 100), new DatosBulto(3, 40, 30, 20, 150)],
            OrigenEvento.PortalComercio, null, ahora);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, Guid.NewGuid(), ahora.AddMinutes(1));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    private sealed record Tenant(Guid? OperadorId, Guid? ComercioId) : ICurrentTenant;
}
