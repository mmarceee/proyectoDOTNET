using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-60, requerimiento 3 (ADR-0006): el seguimiento se guarda en la caché y se borra ante cada cambio de estado. Sin
// sesión, el inquilino de estas consultas es el provisorio (operador Demo), igual para la API y para el consumidor.
public class CacheSeguimientoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task La_caché_guarda_el_seguimiento_y_el_evento_de_cambio_de_estado_la_borra()
    {
        var envio = await EnvioAdmitidoAsync();

        // Primera lectura: se arma y se guarda.
        Assert.Equal("Recibimos tu envío", (await ConsultarAsync(envio.Id))!.Estado);

        // Cambia en la base, pero la caché todavía tiene lo anterior.
        await PasarAlDepositoAsync(envio.Id);
        Assert.Equal("Recibimos tu envío", (await ConsultarAsync(envio.Id))!.Estado);

        // Llega un evento de Envíos con ese envío: el consumidor borra la caché y la próxima lectura ve el estado nuevo.
        await InvalidarAsync(envio.Id);
        var actual = await ConsultarAsync(envio.Id);
        Assert.Equal("En el depósito", actual!.Estado);
        Assert.Equal("Ana", actual.NombreDePila);
        Assert.Equal("Operador Demo", actual.Marca.Operador);
    }

    // RF 35: el resumen conserva EntregadoParcialmente entre visitas; el evento del bulto debe renovar el avance.
    [Fact]
    public async Task Un_evento_de_bulto_actualiza_de_uno_a_dos_de_tres_aunque_el_resumen_no_cambie()
    {
        factory.CreateClient();
        var ahora = DateTimeOffset.UtcNow;
        var lugar = new Ubicacion(-34.9m, -56.16m);
        var ausente = new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
        var reglas = new ReglasEntrega(false, false, 3, TimeSpan.FromHours(24), admiteEntregaParcial: true);
        var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, $"ENV-C-{Guid.NewGuid():N}",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(new DatosBulto(2, 30, 20, 10, 150), 3).ToList(), OrigenEvento.PortalComercio, null, ahora);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
        var b = envio.Bultos;
        envio.RegistrarVisita([new(b[0].Id), new(b[1].Id, ausente), new(b[2].Id, ausente)],
            new PruebaEntrega(Guid.NewGuid(), null, null, null, lugar, ahora), null, null, lugar, reglas, null, ahora);
        using (var scope = factory.Services.CreateScope())
        {
            await using var db = Contexto(scope);
            db.Envios.Add(envio);
            await db.SaveChangesAsync();
        }
        Assert.Equal("1 de 3 bultos entregados", (await ConsultarAsync(envio.Id))!.Avance);

        using (var scope = factory.Services.CreateScope())
        {
            await using var db = Contexto(scope);
            var siguiente = await db.Envios.Include(e => e.Eventos).Include(e => e.Intentos).SingleAsync(e => e.Id == envio.Id);
            siguiente.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora.AddDays(1));
            siguiente.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora.AddDays(1));
            siguiente.RegistrarVisita([new(b[1].Id), new(b[2].Id, ausente)],
                new PruebaEntrega(Guid.NewGuid(), null, null, null, lugar, ahora.AddDays(1)), null, null, lugar, reglas,
                null, ahora.AddDays(1));
            Assert.Equal(ResumenEnvio.EntregadoParcialmente, siguiente.Resumen);
            await db.SaveChangesAsync();
        }
        Assert.Equal("1 de 3 bultos entregados", (await ConsultarAsync(envio.Id))!.Avance);

        using (var scope = factory.Services.CreateScope())
        {
            var consumidor = scope.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(
                InvalidarSeguimiento.Cola(BultoEntregado.Tipo));
            var contenido = JsonSerializer.Serialize(new BultoEntregado(envio.Id, envio.Numero, b[1].Codigo,
                "Reprogramado", ahora.AddDays(1)));
            await consumidor.ProcesarAsync(new MensajeIntegracion(Guid.NewGuid(), BultoEntregado.Tipo,
                DatosIniciales.OperadorDemoId, null, ahora.AddDays(1), null, contenido), CancellationToken.None);
        }
        var actualizado = await ConsultarAsync(envio.Id);
        Assert.Equal("Entregado parcialmente", actualizado!.Estado);
        Assert.Equal("2 de 3 bultos entregados", actualizado.Avance);
        Assert.Equal(2, actualizado.Bultos.Count(bulto => bulto.Estado == "Entregado"));
    }

    private async Task<Logistica.Http.Contracts.Seguimiento.SeguimientoPublicoResponse?> ConsultarAsync(Guid envioId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ConsultarSeguimientoHandler>()
            .HandleAsync(envioId, CancellationToken.None);
    }

    private async Task InvalidarAsync(Guid envioId)
    {
        using var scope = factory.Services.CreateScope();
        var tipo = EnvioEntregado.Tipo;
        var consumidor = scope.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(InvalidarSeguimiento.Cola(tipo));
        var contenido = JsonSerializer.Serialize(new EnvioEntregado(envioId, "ENV", DateTimeOffset.UtcNow, null));
        await consumidor.ProcesarAsync(new MensajeIntegracion(Guid.NewGuid(), tipo, DatosIniciales.OperadorDemoId, null,
            DateTimeOffset.UtcNow, null, contenido), CancellationToken.None);
    }

    private async Task<Envio> EnvioAdmitidoAsync()
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, $"ENV-C-{Guid.NewGuid():N}",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow.AddHours(-1));

        using var scope = factory.Services.CreateScope();
        await using var db = Contexto(scope);
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    private async Task PasarAlDepositoAsync(Guid envioId)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = Contexto(scope);
        var envio = await db.Envios.Include(e => e.Eventos).SingleAsync(e => e.Id == envioId);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static EnviosDbContext Contexto(IServiceScope scope) => ActivatorUtilities.CreateInstance<EnviosDbContext>(
        scope.ServiceProvider, new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
}
