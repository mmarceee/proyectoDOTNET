using System.Diagnostics;
using System.Text;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging;

public sealed class PublicadorOutbox(IServiceScopeFactory ambitos, IConfiguration configuration,
    IEnumerable<SuscripcionIntegracion> suscripciones, ILogger<PublicadorOutbox> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conexion = await RabbitMqTransport.CrearFactory(configuration).CreateConnectionAsync(stoppingToken);
                await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true), stoppingToken);
                await RabbitMqTransport.DeclararAsync(canal, suscripciones, stoppingToken);
                while (canal.IsOpen && !stoppingToken.IsCancellationRequested)
                {
                    await PublicarPendientesAsync(canal, stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogWarning(e, "No se pudo publicar el outbox; los eventos siguen pendientes.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public async Task PublicarPendientesAsync(IChannel canal, CancellationToken ct)
    {
        await using var ambito = ambitos.CreateAsyncScope();
        // El publicador no resuelve entidades del negocio ni usa el tenant provisorio de la API.
        ambito.ServiceProvider.GetRequiredService<InquilinoMensaje>().SinInquilino();
        foreach (var db in ambito.ServiceProvider.GetServices<ModuleDbContext>())
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var schema = db.Model.GetDefaultSchema()!;
            // Nombre tomado del modelo EF del módulo, nunca de una petición o mensaje.
            var tabla = '"' + schema.Replace("\"", "\"\"") + "\".outbox_messages";
            // Sólo tipos con consumidor declarado: el resto queda pendiente para sus próximos CU.
            var tipos = suscripciones.Select(s => s.Tipo).Distinct().ToArray();
            // El único identificador interpolado es el schema EF escapado arriba; los tipos van parametrizados.
#pragma warning disable EF1002
            var pendientes = await db.Outbox.FromSqlRaw($"SELECT * FROM {tabla} WHERE \"PublicadoEn\" IS NULL AND \"Tipo\" = ANY ({{0}}) ORDER BY \"OcurridoEn\" LIMIT 50 FOR UPDATE SKIP LOCKED", (object)tipos).ToListAsync(ct);
#pragma warning restore EF1002
            foreach (var fila in pendientes)
            {
                var mensaje = new MensajeIntegracion(fila.Id, fila.Tipo, fila.OperadorId, fila.ComercioId,
                    fila.OcurridoEn, fila.TraceParent, fila.Contenido);
                using var traza = new Activity("outbox.publicar").SetParentId(fila.TraceParent ?? "").Start();
                // mandatory + confirms: una publicación no enrutada también deja la fila pendiente.
                await canal.BasicPublishAsync(RabbitMqTransport.Exchange, fila.Tipo, mandatory: true,
                    RabbitMqTransport.Propiedades(mensaje), Encoding.UTF8.GetBytes(fila.Contenido), ct);
                db.Entry(fila).Property(m => m.PublicadoEn).CurrentValue = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
