using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

// Entrega parcial (RF 31; cierre técnico, D4 y D5): los lectores que decidían por el estado de compatibilidad del envío
// ahora deciden por sus bultos. El tablero ve el avance de 1/3 a 2/3 aunque el resumen siga siendo Entregado
// parcialmente, y un parcial abierto cuenta como pendiente del comercio (baja de la relación, CU-01).
public sealed class ProgresoPorBultoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task El_tablero_ve_el_avance_por_bulto_aunque_el_resumen_no_cambie()
    {
        using var scope = factory.Services.CreateScope();
        var operadorId = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().OperadorId!.Value;
        var unoDeTres = await GuardarAsync(scope, ParcialmenteEntregado(operadorId, entregados: 1));
        var dosDeTres = await GuardarAsync(scope, ParcialmenteEntregado(operadorId, entregados: 2));

        var filas = (await scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>()
            .ListarParaTableroAsync([unoDeTres.Id, dosDeTres.Id], DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None))
            .ToDictionary(e => e.EnvioId);

        Assert.Equal((1, 3), (filas[unoDeTres.Id].BultosEntregados, filas[unoDeTres.Id].BultosTotal));
        Assert.Equal((2, 3), (filas[dosDeTres.Id].BultosEntregados, filas[dosDeTres.Id].BultosTotal));
        // Ninguno está entregado del todo y los dos esperan todavía una entrega, decida lo que decida el estado de
        // compatibilidad del envío.
        Assert.All(filas.Values, e => Assert.False(e.Entregado));
        Assert.All(filas.Values, e => Assert.True(e.EntregaPendiente));
    }

    [Fact]
    public async Task Un_envio_parcialmente_entregado_sigue_pendiente_para_el_comercio()
    {
        using var scope = factory.Services.CreateScope();
        var operadorId = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().OperadorId!.Value;
        var pendientes = scope.ServiceProvider.GetRequiredService<IEnviosDeComercio>();
        var antes = await pendientes.ContarPendientesAsync(DatosIniciales.ComercioDemoId, CancellationToken.None);

        var parcial = await GuardarAsync(scope, ParcialmenteEntregado(operadorId, entregados: 2));

        Assert.False(parcial.Terminado);
        Assert.Equal(antes + 1, await pendientes.ContarPendientesAsync(DatosIniciales.ComercioDemoId, CancellationToken.None));
    }

    // Tres bultos: se cargan y entregan los primeros "entregados"; el resto sigue asignado a la ruta.
    private static Envio ParcialmenteEntregado(Guid operadorId, int entregados)
    {
        var ahora = DateTimeOffset.UtcNow;
        var envio = Envio.Crear(operadorId, DatosIniciales.ComercioDemoId, $"ENV-P-{Guid.NewGuid():N}".ToUpperInvariant(),
            new Destinatario("Ana", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(new DatosBulto(1, 10, 10, 10, 100), 3).ToArray(), OrigenEvento.PortalComercio, null,
            ahora.AddHours(-3));
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora.AddHours(-2));
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora.AddHours(-2));

        var cargados = envio.Bultos.Take(entregados).Select(b => b.Id).ToArray();
        envio.CargarBultos(cargados, null, ahora.AddHours(-1));
        var lugar = new Ubicacion(-34.9m, -56.16m);
        envio.RegistrarVisita(cargados.Select(id => new ResultadoBulto(id)).ToArray(),
            new PruebaEntrega(Guid.NewGuid(), Guid.NewGuid(), null, null, lugar, ahora), null, null, lugar,
            new ReglasEntrega(false, false, 3, TimeSpan.FromHours(24), true, admiteEntregaParcial: true), null, ahora);
        return envio;
    }

    private static async Task<Envio> GuardarAsync(IServiceScope scope, Envio envio)
    {
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return envio;
    }
}
