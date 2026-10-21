using System.Security.Cryptography;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Infrastructure.Security;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

public sealed class ConfigurarSuscripcionesAvisoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly string Clave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly InquilinoFijo ComercioDemo = new(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId);
    private static CrearSuscripcionAvisoCommand Comando(string? url = null) => new(
        url ?? $"https://comercio.example/avisos/{Guid.NewGuid():N}", [TipoEventoAviso.CambioDeEstado, TipoEventoAviso.Devolucion]);

    [Fact]
    public async Task Crea_con_relacion_de_sesion_y_guarda_solo_el_cifrado_y_regenerar_reemplaza_el_secreto()
    {
        using var api = CrearApi();
        SecretoSuscripcionAviso creada;
        await using (var scope = api.Services.CreateAsyncScope())
            creada = await scope.ServiceProvider.GetRequiredService<CrearSuscripcionAvisoHandler>().HandleAsync(Comando(), default);

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SeguimientoDbContext>();
            var guardada = await db.SuscripcionesAviso.SingleAsync(s => s.Id == creada.Id);
            var relacion = await scope.ServiceProvider.GetRequiredService<AdministracionDbContext>().RelacionesComerciales.SingleAsync();
            Assert.Equal(relacion.Id, guardada.RelacionComercialId);
            Assert.Equal(ComercioDemo.OperadorId, guardada.OperadorId);
            Assert.Equal(ComercioDemo.ComercioId, guardada.ComercioId);
            Assert.NotEqual(creada.SecretoFirma, guardada.SecretoFirma);
            Assert.Equal(creada.SecretoFirma, Descifrar(guardada));
            Assert.DoesNotContain(creada.SecretoFirma, creada.ToString()!);
            guardada.Desactivar();
            await db.SaveChangesAsync();
        }

        SecretoSuscripcionAviso regenerada;
        await using (var scope = api.Services.CreateAsyncScope())
            regenerada = (await scope.ServiceProvider.GetRequiredService<RegenerarSecretoAvisoHandler>()
                .HandleAsync(creada.Id, default))!;

        Assert.NotEqual(creada.SecretoFirma, regenerada.SecretoFirma);
        await using var verificacion = api.Services.CreateAsyncScope();
        var actual = await verificacion.ServiceProvider.GetRequiredService<SeguimientoDbContext>().SuscripcionesAviso.SingleAsync(s => s.Id == creada.Id);
        Assert.Equal(regenerada.SecretoFirma, Descifrar(actual));
        Assert.False(actual.Activa);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Crear_sin_clave_o_con_direccion_invalida_no_persiste_suscripcion(bool sinClave)
    {
        using var api = CrearApi(clave: sinClave ? "" : Clave);
        var comando = Comando(sinClave ? null : "http://comercio.example/avisos");
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<CrearSuscripcionAvisoHandler>();
            if (sinClave) await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(comando, default));
            else await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(comando, default));
        }
        await using var lectura = api.Services.CreateAsyncScope();
        Assert.False(await lectura.ServiceProvider.GetRequiredService<SeguimientoDbContext>().SuscripcionesAviso
            .AnyAsync(s => s.UrlDestino == comando.UrlDestino));
    }

    [Fact]
    public async Task Sin_relacion_comercial_no_se_crea_la_suscripcion()
    {
        using var api = CrearApi(new InquilinoFijo(Guid.NewGuid(), DatosIniciales.ComercioDemoId));
        await using var scope = api.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<DomainException>(() => scope.ServiceProvider.GetRequiredService<CrearSuscripcionAvisoHandler>()
            .HandleAsync(Comando(), default));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SeguimientoDbContext>().SuscripcionesAviso.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_regenera_suscripciones_de_otro_operador_o_comercio(bool otroOperador)
    {
        using var api = CrearApi();
        SecretoSuscripcionAviso creada;
        await using (var scope = api.Services.CreateAsyncScope())
            creada = await scope.ServiceProvider.GetRequiredService<CrearSuscripcionAvisoHandler>().HandleAsync(Comando(), default);
        using var ajena = CrearApi(new InquilinoFijo(otroOperador ? Guid.NewGuid() : ComercioDemo.OperadorId,
            otroOperador ? ComercioDemo.ComercioId : Guid.NewGuid()));
        await using (var scope = ajena.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<RegenerarSecretoAvisoHandler>();
            Assert.Null(await handler.HandleAsync(creada.Id, default));
            Assert.Null(await handler.HandleAsync(Guid.NewGuid(), default));
        }
        await using var lectura = api.Services.CreateAsyncScope();
        Assert.Equal(creada.SecretoFirma, Descifrar(await lectura.ServiceProvider.GetRequiredService<SeguimientoDbContext>()
            .SuscripcionesAviso.SingleAsync(s => s.Id == creada.Id)));
    }

    [Fact]
    public async Task Fallo_despues_de_guardar_revierte_creacion_y_regeneracion()
    {
        using var api = CrearApi();
        SecretoSuscripcionAviso creada;
        await using (var scope = api.Services.CreateAsyncScope())
            creada = await scope.ServiceProvider.GetRequiredService<CrearSuscripcionAvisoHandler>().HandleAsync(Comando(), default);

        using var baseFalla = CrearApi();
        using var falla = baseFalla.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddScoped<ISuscripcionesAvisoRepository>(sp =>
                new FalloAlGuardar(new SuscripcionesAvisoRepository(sp.GetRequiredService<SeguimientoDbContext>())))));
        var comando = Comando();
        await using (var scope = falla.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CrearSuscripcionAvisoHandler>()
                .HandleAsync(comando, default));
        await using (var scope = falla.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<RegenerarSecretoAvisoHandler>()
                .HandleAsync(creada.Id, default));

        await using var lectura = api.Services.CreateAsyncScope();
        var db = lectura.ServiceProvider.GetRequiredService<SeguimientoDbContext>();
        Assert.False(await db.SuscripcionesAviso.AnyAsync(s => s.UrlDestino == comando.UrlDestino));
        Assert.Equal(creada.SecretoFirma, Descifrar(await db.SuscripcionesAviso.SingleAsync(s => s.Id == creada.Id)));
    }

    [Fact]
    public async Task Una_regeneracion_con_lectura_anterior_no_pisa_el_nuevo_secreto()
    {
        using var api = CrearApi();
        SecretoSuscripcionAviso creada;
        await using (var scope = api.Services.CreateAsyncScope())
            creada = await scope.ServiceProvider.GetRequiredService<CrearSuscripcionAvisoHandler>().HandleAsync(Comando(), default);
        await using var anterior = api.Services.CreateAsyncScope();
        var dbAnterior = anterior.ServiceProvider.GetRequiredService<SeguimientoDbContext>();
        var vieja = await dbAnterior.SuscripcionesAviso.SingleAsync(s => s.Id == creada.Id);
        SecretoSuscripcionAviso regenerada;
        await using (var scope = api.Services.CreateAsyncScope())
            regenerada = (await scope.ServiceProvider.GetRequiredService<RegenerarSecretoAvisoHandler>().HandleAsync(creada.Id, default))!;
        vieja.RegenerarSecreto("otra-version-cifrada");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbAnterior.SaveChangesAsync());
        await using var lectura = api.Services.CreateAsyncScope();
        Assert.Equal(regenerada.SecretoFirma, Descifrar(await lectura.ServiceProvider.GetRequiredService<SeguimientoDbContext>()
            .SuscripcionesAviso.SingleAsync(s => s.Id == creada.Id)));
    }

    private WebApplicationFactory<Program> CrearApi(InquilinoFijo? tenant = null, string? clave = null) =>
        factory.WithWebHostBuilder(b => b.UseSetting("Seguimiento:Avisos:ClaveCifrado", clave ?? Clave)
            .ConfigureServices(s => s.AddScoped<ICurrentTenant>(_ => tenant ?? ComercioDemo)));

    private static string Descifrar(SuscripcionAviso s) =>
        new SecretosAviso(Clave).Descifrar(s.SecretoFirma, s.OperadorId, s.ComercioId);

    private sealed class FalloAlGuardar(ISuscripcionesAvisoRepository real) : ISuscripcionesAvisoRepository
    {
        public void Agregar(SuscripcionAviso s) => real.Agregar(s);
        public Task<SuscripcionAviso?> ObtenerAsync(Guid id, CancellationToken ct) => real.ObtenerAsync(id, ct);
        public Task<IReadOnlyList<SuscripcionAviso>> ListarAsync(CancellationToken ct) => real.ListarAsync(ct);
        public Task<IReadOnlyList<SuscripcionAviso>> ListarActivasAsync(Guid comercioId, CancellationToken ct) =>
            real.ListarActivasAsync(comercioId, ct);
        public async Task GuardarAsync(CancellationToken ct)
        {
            await real.GuardarAsync(ct);
            throw new InvalidOperationException("Falla simulada antes de confirmar la transacción.");
        }
    }
}
