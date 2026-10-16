using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging;

public sealed class ConsumidorRabbitMq(IServiceScopeFactory ambitos, IConfiguration configuration,
    IEnumerable<SuscripcionIntegracion> suscripciones, ILogger<ConsumidorRabbitMq> logger) : BackgroundService
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
                // Canal secuencial: nunca hay publicaciones o acks concurrentes sobre el mismo canal.
                while (canal.IsOpen && !stoppingToken.IsCancellationRequested)
                {
                    foreach (var s in suscripciones)
                    {
                        var entrega = await canal.BasicGetAsync(s.Cola, autoAck: false, stoppingToken);
                        if (entrega is not null) await ProcesarEntregaAsync(canal, s, entrega, stoppingToken);
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                // Al cerrar el canal, cualquier entrega aún sin ack vuelve a la cola.
                logger.LogWarning(e, "El consumidor de avisos se reconectará; las entregas sin confirmar se conservan.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ProcesarEntregaAsync(IChannel canal, SuscripcionIntegracion s, BasicGetResult entrega, CancellationToken ct)
    {
        try
        {
            var mensaje = RabbitMqTransport.Leer(entrega.BasicProperties, entrega.Body);
            if (mensaje.Tipo != s.Tipo) throw new InvalidDataException("Tipo de evento inesperado.");
            using var traza = new Activity("mensaje.consumir").SetParentId(mensaje.TraceParent ?? "").Start();
            await using var ambito = ambitos.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
            await ambito.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(s.Cola).ProcesarAsync(mensaje, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            var propiedades = new BasicProperties(entrega.BasicProperties);
            propiedades.Headers = propiedades.Headers is null ? [] : new Dictionary<string, object?>(propiedades.Headers);
            var intento = propiedades.Headers.TryGetValue("intento", out var valor) ? Convert.ToInt32(valor) : 0;
            var invalido = e is InvalidDataException or JsonException;
            var destino = invalido || intento >= RabbitMqTransport.Esperas.Length
                ? RabbitMqTransport.Fallidos : RabbitMqTransport.ColaEspera(s.Cola, intento);
            propiedades.Headers["intento"] = intento + 1;
            propiedades.Headers["cola-origen"] = s.Cola;
            propiedades.Headers["ultimo-error"] = e.GetType().Name;
            propiedades.Persistent = true;
            logger.LogWarning(e, "Mensaje {MessageId} de {Cola} enviado a {Destino}, intento {Intento}.",
                propiedades.MessageId, s.Cola, destino, intento + 1);
            // No se confirma el original hasta que el broker confirme la copia durable.
            await canal.BasicPublishAsync("", destino, mandatory: true, propiedades, entrega.Body, ct);
        }
        await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct);
    }
}
