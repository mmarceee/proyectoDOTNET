using System.Security.Claims;
using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

// RF 36, lado de Envíos: la visita publica un evento por bulto (para los avisos v2) además de los eventos del envío, que
// siguen igual para los consumidores v1. Clase propia, con su base: publica para Demo una versión de reglas que admite
// la entrega parcial, como EntregaParcialReglasTests.
public sealed class EventosDeBultoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid Demo = DatosIniciales.OperadorDemoId;
    private static readonly ArchivoRecibido Firma = new("image/png", Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD8kAAAAASUVORK5CYII="));

    // Un bulto entregado, uno reprogramado y uno en devolución. Para v1, el intento fallido de siempre y ningún
    // EnvioEntregado. En la revisita se entrega el reprogramado: el envío queda Entregado por compatibilidad, pero un
    // bulto sigue en devolución, así que v1 tampoco recibe una entrega completa que no ocurrió.
    [Fact]
    public async Task Una_visita_mixta_publica_cada_bulto_y_nunca_una_entrega_completa_que_no_ocurrio()
    {
        var reglas = await PublicarEntregaParcialAsync();
        var envio = await EnvioEnTransitoAsync(bultos: 3, reglas);
        var codigos = envio.Bultos.ToDictionary(b => b.Id, b => b.Codigo);
        var (entregado, reprogramado, devuelto) = (envio.Bultos[0].Id, envio.Bultos[1].Id, envio.Bultos[2].Id);
        var (ausente, rechazado) = (await MotivoAsync("AUSENTE"), await MotivoAsync("RECHAZADO"));

        var primera = await RegistrarAsync(envio.Id,
            [new(entregado, null), new(reprogramado, ausente), new(devuelto, rechazado)], minutosAntes: 30);

        Assert.Equal(ResultadoRegistro.Aplicado, primera.Resultado);
        var eventos = await EventosAsync(envio.Id);
        Assert.DoesNotContain(eventos, e => e.Tipo == EnvioEntregado.Tipo);
        Assert.Equal(2, eventos.Count(e => e.Tipo == IntentoFallidoRegistrado.Tipo));
        var bultoEntregado = Leer<BultoEntregado>(Assert.Single(eventos, e => e.Tipo == BultoEntregado.Tipo));
        Assert.Equal((codigos[entregado], "Reprogramado"), (bultoEntregado.CodigoBulto, bultoEntregado.EstadoEnvio));
        var noEntregados = eventos.Where(e => e.Tipo == BultoNoEntregado.Tipo).Select(Leer<BultoNoEntregado>)
            .OrderBy(e => e.CodigoBulto).ToList();
        Assert.Equal(
            [(codigos[reprogramado], "AUSENTE", "Reprogramado"), (codigos[devuelto], "RECHAZADO", "EnDevolucion")],
            noEntregados.Select(e => (e.CodigoBulto, e.Motivo, e.EstadoBulto)).OrderBy(e => e.Item1));
        // CU-31 por bulto: el bulto en devolución abre la devolución aunque el envío siga Reprogramado.
        await using (var db = Contexto<EnviosDbContext>(Demo))
        {
            var devolucion = await db.Devoluciones.AsNoTracking().SingleAsync(d => d.EnvioId == envio.Id);
            Assert.Equal(("Intento fallido: RECHAZADO.", Logistica.Modules.Envios.Domain.Devoluciones.EstadoDevolucion.Pendiente),
                (devolucion.Motivo, devolucion.Estado));
        }

        await VolverATransitoAsync(envio.Id);
        var revisita = await RegistrarAsync(envio.Id, [new(reprogramado, null)], minutosAntes: 5);

        Assert.Equal(ResultadoRegistro.Aplicado, revisita.Resultado);
        eventos = await EventosAsync(envio.Id);
        Assert.DoesNotContain(eventos, e => e.Tipo == EnvioEntregado.Tipo);
        Assert.Equal([codigos[entregado], codigos[reprogramado]],
            eventos.Where(e => e.Tipo == BultoEntregado.Tipo).Select(e => Leer<BultoEntregado>(e).CodigoBulto).Order());
    }

    // Una entrega completa sigue publicando EnvioEntregado, como antes, y además un BultoEntregado por bulto.
    [Fact]
    public async Task Una_entrega_completa_publica_EnvioEntregado_y_un_evento_por_bulto()
    {
        var envio = await EnvioEnTransitoAsync(bultos: 2);

        var resultado = await RegistrarAsync(envio.Id, envio.Bultos.Select(b => new ResultadoBultoARegistrar(b.Id, null)).ToList(),
            minutosAntes: 5);

        Assert.Equal(ResultadoRegistro.Aplicado, resultado.Resultado);
        var eventos = await EventosAsync(envio.Id);
        Assert.Single(eventos, e => e.Tipo == EnvioEntregado.Tipo);
        Assert.Equal(envio.Bultos.Select(b => b.Codigo).Order(),
            eventos.Where(e => e.Tipo == BultoEntregado.Tipo).Select(e => Leer<BultoEntregado>(e).CodigoBulto).Order());
        Assert.All(eventos.Where(e => e.Tipo == BultoEntregado.Tipo),
            e => Assert.Equal("Entregado", Leer<BultoEntregado>(e).EstadoEnvio));
    }

    // ---- Ayudas ----

    private sealed record EventoGuardado(string Tipo, string Contenido);

    private static T Leer<T>(EventoGuardado evento) => JsonSerializer.Deserialize<T>(evento.Contenido)!;

    private async Task<List<EventoGuardado>> EventosAsync(Guid envioId)
    {
        await using var db = Contexto<EnviosDbContext>(Demo);
        return (await db.Outbox.AsNoTracking().ToListAsync())
            .Where(m => m.Contenido.Contains(envioId.ToString()))
            .Select(m => new EventoGuardado(m.Tipo, m.Contenido)).ToList();
    }

    private async Task<RegistroEnCalle> RegistrarAsync(Guid envioId, IReadOnlyList<ResultadoBultoARegistrar> resultados,
        int minutosAntes)
    {
        using var scope = Scope();
        var conEntrega = resultados.Any(r => r.MotivoId is null);
        return await scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>().RegistrarVisitaAsync(
            new VisitaARegistrar(envioId, DateTimeOffset.UtcNow.AddMinutes(-minutosAntes), -34.9m, -56.16m, resultados,
                conEntrega ? Firma : null, null, null, null, null, null),
            CancellationToken.None);
    }

    // Una versión 2 de las reglas de Demo, vigente desde ahora, que admite la entrega parcial y vuelve a visitar sin
    // recargo. El envío de la prueba la guarda como su versión: no depende de la caché del puntero a la vigente.
    private async Task<Guid> PublicarEntregaParcialAsync()
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        await using var db = Contexto<AdministracionDbContext>(Demo);
        var anterior = await db.VersionesReglas.SingleAsync(v => v.Numero == 1);
        var valores = anterior.Valores() with
        {
            EntregaParcial = new ReglasEntregaParcial(true, PoliticaPendientes.NuevaVisitaSinRecargo, 0),
        };
        var nueva = VersionReglas.CrearBorrador(Demo, 2, valores,
        [
            new DatosMotivo("AUSENTE", "Destinatario ausente", Reprogramable: true, ExigeEvidencia: false),
            new DatosMotivo("RECHAZADO", "El destinatario rechazó el envío", Reprogramable: false, ExigeEvidencia: false),
        ]);
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-1);
        nueva.Publicar(ahora, ahora, anterior);
        db.VersionesReglas.Add(nueva);
        await db.SaveChangesAsync();
        return nueva.Id;
    }

    // Un motivo de la versión 2 (la vigente).
    private async Task<Guid> MotivoAsync(string codigo)
    {
        await using var db = Contexto<AdministracionDbContext>(Demo);
        var vigente = await db.VersionesReglas.Where(v => v.Numero == 2).Select(v => v.Id).SingleAsync();
        return await db.MotivosNoEntrega.Where(m => m.VersionReglasId == vigente && m.Codigo == codigo)
            .Select(m => m.Id).SingleAsync();
    }

    private async Task<Envio> EnvioEnTransitoAsync(int bultos, Guid? versionReglasId = null)
    {
        factory.CreateClient();
        var ahora = DateTimeOffset.UtcNow.AddHours(-2);
        var envio = Envio.Crear(Demo, DatosIniciales.ComercioDemoId, $"ENV-B-{Guid.NewGuid():N}".ToUpperInvariant(),
            new Destinatario("Ana Pérez", "099123456"), new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(new DatosBulto(2, 30, 20, 10, 150), bultos).ToList(), OrigenEvento.PortalComercio, null, ahora,
            versionReglasId: versionReglasId);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);

        await using var db = Contexto<EnviosDbContext>(Demo);
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    // La revisita (RF 34) todavía no tiene operación propia: el envío reprogramado vuelve a ruta y sale.
    private async Task VolverATransitoAsync(Guid envioId)
    {
        await using var db = Contexto<EnviosDbContext>(Demo);
        var envio = await db.Envios.SingleAsync(e => e.Id == envioId);
        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, DateTimeOffset.UtcNow.AddMinutes(-20));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, DateTimeOffset.UtcNow.AddMinutes(-15));
        await db.SaveChangesAsync();
    }

    // Un ámbito con el operador como claim de la sesión, como personal del operador (ADR-0002, sección 2.5).
    private IServiceScope Scope()
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("operador_id", Demo.ToString())], "Prueba")),
        };
        return scope;
    }

    private T Contexto<T>(Guid operadorId) where T : Logistica.BuildingBlocks.Infrastructure.Persistence.ModuleDbContext
    {
        var scope = factory.Services.CreateScope();
        return ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(operadorId, null));
    }
}
