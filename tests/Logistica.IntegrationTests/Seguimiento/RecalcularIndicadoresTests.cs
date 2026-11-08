using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Domain.Indicadores;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-71: los eventos de Envíos alimentan la tabla de lectura de los indicadores. Cada prueba usa su propio operador.
public sealed class RecalcularIndicadoresTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Registra_entregas_e_intentos_fallidos_sin_contar_dos_veces_el_mismo_evento()
    {
        var (operador, comercio, zona, repartidor) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (aTiempo, tarde) = (Guid.NewGuid(), Guid.NewGuid());
        await AltaAsync(operador, comercio, aTiempo, zona, Ahora.AddHours(2));
        await AltaAsync(operador, comercio, tarde, zona, Ahora.AddHours(-1));

        // La entrega y el intento fallido los publica el repartidor: el sobre no trae comercio.
        var entrega = Mensaje(EnvioEntregado.Tipo,
            new EnvioEntregado(aTiempo, "ENV-71-A", Ahora, Guid.NewGuid(), repartidor), operador, null);
        await ProcesarAsync(entrega);
        await ProcesarAsync(entrega);
        await ProcesarAsync(Mensaje(IntentoFallidoRegistrado.Tipo,
            new IntentoFallidoRegistrado(tarde, "ENV-71-B", 1, "Ausente", "Reprogramado", Ahora, Ahora.AddDays(1), repartidor),
            operador, null));
        await ProcesarAsync(Mensaje(EnvioEntregado.Tipo,
            new EnvioEntregado(tarde, "ENV-71-B", Ahora, Guid.NewGuid(), repartidor), operador, null));

        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = Contexto(scope, operador);
        var registros = await db.RegistrosCumplimiento.AsNoTracking().ToListAsync();
        Assert.Equal(5, registros.Count);
        Assert.All(registros, r => Assert.Equal((comercio, (Guid?)zona), (r.ComercioId, r.ZonaId)));
        Assert.Equal(2, registros.Count(r => r.Tipo == TipoRegistroCumplimiento.Alta));

        var entregas = registros.Where(r => r.Tipo == TipoRegistroCumplimiento.Entrega).ToList();
        Assert.Equal(new bool?[] { true }, entregas.Where(r => r.EnvioId == aTiempo).Select(r => r.ATiempo));
        Assert.Equal(new bool?[] { false }, entregas.Where(r => r.EnvioId == tarde).Select(r => r.ATiempo));
        Assert.All(entregas, r => Assert.Equal(repartidor, r.RepartidorId));

        var fallo = Assert.Single(registros, r => r.Tipo == TipoRegistroCumplimiento.IntentoFallido);
        Assert.Equal((tarde, "Ausente", (Guid?)repartidor), (fallo.EnvioId, fallo.Motivo, fallo.RepartidorId));
    }

    [Fact]
    public async Task Los_envios_de_prueba_no_cuentan_y_una_entrega_sin_alta_se_reintenta()
    {
        var (operador, comercio, prueba, sinAlta) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await ProcesarAsync(Mensaje(EnvioAdmitido.Tipo,
            new EnvioAdmitido(prueba, "ENV-71-P", Ahora, EsPrueba: true, EntregaComprometidaHasta: Ahora.AddHours(1)),
            operador, comercio));
        await ProcesarAsync(Mensaje(EnvioEntregado.Tipo, new EnvioEntregado(prueba, "ENV-71-P", Ahora, null), operador, null));

        // Sin orden garantizado entre colas: si la entrega llega antes que el alta, el reintento la espera.
        await Assert.ThrowsAsync<EnvioNoRegistradoException>(() => ProcesarAsync(
            Mensaje(EnvioEntregado.Tipo, new EnvioEntregado(sinAlta, "ENV-71-S", Ahora, null), operador, null)));

        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = Contexto(scope, operador);
        Assert.False(await db.RegistrosCumplimiento.AnyAsync());
        Assert.True((await db.EnviosSeguidos.SingleAsync(e => e.Id == prueba)).EsPrueba);
    }

    private Task AltaAsync(Guid operador, Guid comercio, Guid envio, Guid zona, DateTimeOffset limite) =>
        ProcesarAsync(Mensaje(EnvioAdmitido.Tipo,
            new EnvioAdmitido(envio, $"ENV-{envio:N}"[..20], Ahora.AddDays(-1), ZonaId: zona, EntregaComprometidaHasta: limite),
            operador, comercio));

    private static SeguimientoDbContext Contexto(AsyncServiceScope scope, Guid operador) =>
        ActivatorUtilities.CreateInstance<SeguimientoDbContext>(scope.ServiceProvider, new InquilinoFijo(operador, null));

    private static MensajeIntegracion Mensaje<T>(string tipo, T contenido, Guid operador, Guid? comercio) =>
        new(Guid.CreateVersion7(), tipo, operador, comercio, Ahora, null, JsonSerializer.Serialize(contenido));

    private async Task ProcesarAsync(MensajeIntegracion mensaje)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
        await scope.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(RegistrarCumplimiento.Cola(mensaje.Tipo))
            .ProcesarAsync(mensaje, default);
    }
}
