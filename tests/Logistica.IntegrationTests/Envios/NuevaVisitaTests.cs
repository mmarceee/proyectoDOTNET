using System.Security.Claims;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

// Consumidores de EstadoEnvio para la nueva visita (RF 34; decisiones-entrega-parcial.md, sección 6, punto 8), contra la
// base real: después de entregar 2 de 3 bultos, Planificación y carga sólo ven el pendiente. La lectura base de la hoja
// conserva los estados y Ejecución filtra el manifiesto HTTP por ruta (IniciarRendicionTests y Rf32PwaTests).
public sealed class NuevaVisitaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid Demo = DatosIniciales.OperadorDemoId;

    [Fact]
    public async Task La_nueva_visita_planifica_y_carga_el_pendiente_y_conserva_el_contexto_para_la_hoja()
    {
        var (envioId, pendiente) = await EntregadoParcialmenteAsync();

        using (var scope = Scope())
        {
            var envios = scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>();
            var planificable = Assert.Single(await envios.ObtenerParaPlanificacionAsync([envioId], CancellationToken.None));
            Assert.Equal([pendiente.Id], planificable.Bultos.Select(b => b.Id));
        }

        // Planificación lo asigna a una ruta nueva: el puente mueve sólo el pendiente.
        await TransicionarAsync(envioId, EstadoEnvio.AsignadoARuta);

        using (var scope = Scope())
        {
            var envios = scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>();
            var carga = Assert.Single(await envios.ConsultarCargaAsync([envioId], CancellationToken.None));
            Assert.Equal("AsignadoARuta", carga.Estado);
            Assert.Equal([pendiente.Codigo], carga.Bultos.Select(b => b.Codigo));

            var hoja = Assert.Single(await scope.ServiceProvider.GetRequiredService<IEnviosHojaRuta>()
                .ConsultarAsync([envioId], CancellationToken.None));
            // Esta lectura base conserva los estados. La hoja HTTP se filtra por el manifiesto de la ruta
            // (IniciarRendicionTests), evitando perder las entregas que ocurrieron en la visita actual.
            Assert.Equal(3, hoja.Bultos.Count);
            Assert.Equal("AsignadoARuta", Assert.Single(hoja.Bultos, b => b.Id == pendiente.Id).Estado);
            Assert.All(hoja.Bultos.Where(b => b.Id != pendiente.Id), b => Assert.Equal("Entregado", b.Estado));
        }
    }

    [Fact]
    public async Task Sin_entrega_parcial_las_consultas_siguen_viendo_todos_los_bultos()
    {
        var envioId = await GuardarAsync(EnvioEnTransito(3));

        using var scope = Scope();
        var envios = scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>();
        Assert.Equal(3, Assert.Single(await envios.ConsultarCargaAsync([envioId], CancellationToken.None)).Bultos.Count);
        Assert.Equal(3, Assert.Single(await scope.ServiceProvider.GetRequiredService<IEnviosHojaRuta>()
            .ConsultarAsync([envioId], CancellationToken.None)).Bultos.Count);
    }

    // Un envío de tres bultos: en la primera visita se entregan dos y el tercero queda ausente, reprogramado.
    private async Task<(Guid EnvioId, Bulto Pendiente)> EntregadoParcialmenteAsync()
    {
        var envio = EnvioEnTransito(3);
        var b = envio.Bultos;
        var lugar = new Ubicacion(-34.9m, -56.16m);
        var ausente = new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
        var reglas = new ReglasEntrega(exigeFoto: false, exigeNombreYDocumento: false, maxIntentos: 3,
            TimeSpan.FromHours(24), admiteEntregaParcial: true);
        envio.RegistrarVisita([new(b[0].Id), new(b[1].Id), new(b[2].Id, ausente)],
            new PruebaEntrega(Guid.NewGuid(), null, null, null, lugar, DateTimeOffset.UtcNow), null, null, lugar, reglas, null,
            DateTimeOffset.UtcNow);
        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
        return (await GuardarAsync(envio), b[2]);
    }

    private static Envio EnvioEnTransito(int bultos)
    {
        var ahora = DateTimeOffset.UtcNow.AddHours(-3);
        var envio = Envio.Crear(Demo, DatosIniciales.ComercioDemoId, $"ENV-NV-{Guid.NewGuid():N}".ToUpperInvariant(),
            new Destinatario("Ana Pérez", "099123456"), new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(new DatosBulto(2, 30, 20, 10, 150), bultos).ToList(), OrigenEvento.PortalComercio, null, ahora);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
        return envio;
    }

    private async Task<Guid> GuardarAsync(Envio envio)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        await using var db = Contexto();
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio.Id;
    }

    private async Task TransicionarAsync(Guid envioId, EstadoEnvio nuevo)
    {
        await using var db = Contexto();
        var envio = await db.Envios.SingleAsync(e => e.Id == envioId);
        envio.Transicionar(nuevo, OrigenEvento.Backoffice, null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    // Un ámbito con el operador de Demo como claim de la sesión, como el personal del operador (ADR-0002, sección 2.5).
    private IServiceScope Scope()
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("operador_id", Demo.ToString())], "Prueba")),
        };
        return scope;
    }

    private EnviosDbContext Contexto()
    {
        var scope = factory.Services.CreateScope();
        return ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider, new InquilinoFijo(Demo, null));
    }
}
