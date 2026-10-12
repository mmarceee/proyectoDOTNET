using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.BuildingBlocks;

// Base exclusiva y descartable: comprueba traer main cuando CU-41 ya estaba migrado.
public sealed class ActualizacionOutboxCU41Tests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Actualizar_conserva_retiros_y_mensajes_anteriores_del_outbox()
    {
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var operador = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().OperadorId!.Value;
        var envioId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;
        var ruta = Ruta.Crear(operador, new DateOnly(2039, 1, 1), Guid.NewGuid(), Guid.NewGuid(), [new EnvioRuta(envioId, "ENV-TEST", null, null, null, false, [])], ahora);
        ruta.QuitarEnvio(envioId);
        var retiro = RetiroEnvioRuta.Registrar(ruta, envioId, "ENV-PRUEBA-MIGRACION", 1, null, ahora);
        db.Rutas.Add(ruta); db.Retiros.Add(retiro);
        await db.SaveChangesAsync();

        // Reproduce el esquema previo: CU-41 aplicado, sin la tabla Outbox de Planificación.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE planificacion.outbox_messages");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM planificacion.\"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261012124158_OutboxPorModulo'");
        await db.Database.MigrateAsync();
        Assert.Equal("ENV-PRUEBA-MIGRACION", (await db.Retiros.AsNoTracking().SingleAsync(r => r.Id == retiro.Id)).NumeroEnvio);
        Assert.Empty(await db.Outbox.ToListAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var envios = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        await envios.GetService<IMigrator>().MigrateAsync("20261009153003_PlanificacionCU40");
        var mensajeId = Guid.NewGuid();
        const string contenido = "{\"EnvioId\":\"anterior\"}";
        await envios.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO envios.outbox_messages (\"Id\", \"OperadorId\", \"Tipo\", \"Contenido\", \"OcurridoEn\") VALUES ({mensajeId}, {operador}, {"PruebaAnterior.v1"}, {contenido}, {ahora})");
        await envios.Database.MigrateAsync();
        var mensaje = await envios.Outbox.SingleAsync(m => m.Id == mensajeId);
        Assert.Equal(contenido, mensaje.Contenido);
        Assert.Equal(operador, mensaje.OperadorId);
        Assert.Null(mensaje.PublicadoEn);
        Assert.Empty(await envios.Database.GetPendingMigrationsAsync());
    }
}
