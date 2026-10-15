using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

// El contrato que usa Planificación: después de un intento fallido reprogramado (CU-18), el envío llega con el
// instante desde el que se puede volver a intentar. Guardado en PostgreSQL y leído por IEnviosModuleApi.
public sealed class EnvioPlanificableTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Un_envio_reprogramado_tras_un_intento_fallido_llega_a_Planificacion_con_su_ReintentoDesde()
    {
        using var scope = factory.Services.CreateScope();
        var operadorId = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().OperadorId!.Value;
        var fallo = new DateTimeOffset(2026, 10, 22, 11, 30, 0, TimeSpan.FromHours(-3));
        var reglas = new ReglasEntrega(false, false, maxIntentos: 3, TimeSpan.FromHours(24));
        var ausente = new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);

        var envio = Envio.Crear(operadorId, Guid.NewGuid(), $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 100)], OrigenEvento.PortalComercio, null, fallo.AddDays(-1));
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, fallo.AddHours(-5));
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, fallo.AddHours(-4));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, fallo.AddHours(-3));
        envio.RegistrarIntentoFallido(ausente, null, null, new Ubicacion(-34.9m, -56.16m), reglas, null, fallo);

        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var planificable = Assert.Single(await scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>()
            .ObtenerParaPlanificacionAsync([envio.Id], CancellationToken.None));

        Assert.Equal("Reprogramado", planificable.Estado);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero), planificable.ReintentoDesde);
    }
}
