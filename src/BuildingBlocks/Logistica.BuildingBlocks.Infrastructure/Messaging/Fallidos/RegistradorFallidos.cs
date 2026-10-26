using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;

// Registrador de fallidos (ADR-0003, sección 2.6): un consumidor en segundo plano en la API que vacía logistica.fallidos
// y guarda cada mensaje en mensajeria.mensajes_fallidos, con el inquilino de su sobre. Un mensaje sin inquilino no
// pertenece a ningún operador: queda en logistica.fallidos.sin-inquilino, visible sólo en la consola de RabbitMQ.
public sealed class RegistradorFallidos(IServiceScopeFactory ambitos, IConfiguration configuration, TimeProvider reloj,
    ILogger<RegistradorFallidos> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conexion = await RabbitMqTransport.CrearFactory(configuration).CreateConnectionAsync(stoppingToken);
                await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true), stoppingToken);
                await DeclararAsync(canal, stoppingToken);
                while (canal.IsOpen && !stoppingToken.IsCancellationRequested)
                {
                    if (!await RegistrarSiguienteAsync(canal, stoppingToken))
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                // Al cerrar el canal, el mensaje sin confirmar vuelve a logistica.fallidos.
                logger.LogWarning(e, "El registrador de fallidos se reconectará; los mensajes siguen en la cola.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public static async Task DeclararAsync(IChannel canal, CancellationToken ct)
    {
        await canal.QueueDeclareAsync(RabbitMqTransport.Fallidos, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await canal.QueueDeclareAsync(RabbitMqTransport.FallidosSinInquilino, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
    }

    // Registra un mensaje de la cola de fallidos. false si estaba vacía.
    public async Task<bool> RegistrarSiguienteAsync(IChannel canal, CancellationToken ct)
    {
        var entrega = await canal.BasicGetAsync(RabbitMqTransport.Fallidos, autoAck: false, ct);
        if (entrega is null) return false;

        MensajeIntegracion mensaje;
        try
        {
            mensaje = RabbitMqTransport.Leer(entrega.BasicProperties, entrega.Body);
        }
        catch (InvalidDataException)
        {
            // Primero la copia confirmada por el broker, después el ack del original: el mensaje nunca se pierde.
            await canal.BasicPublishAsync("", RabbitMqTransport.FallidosSinInquilino, mandatory: true,
                new BasicProperties(entrega.BasicProperties), entrega.Body, ct);
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct);
            return true;
        }

        var cola = RabbitMqTransport.Cabecera(entrega.BasicProperties, "cola-origen") ?? "desconocida";
        var error = RabbitMqTransport.Cabecera(entrega.BasicProperties, "ultimo-error");
        await using (var ambito = ambitos.CreateAsyncScope())
        {
            // El inquilino se fija antes de resolver el DbContext, como en cualquier consumidor (ADR-0003, sección 2.5).
            ambito.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
            var db = ambito.ServiceProvider.GetRequiredService<MensajeriaDbContext>();
            var ahora = reloj.GetUtcNow();
            var fila = await db.MensajesFallidos.SingleOrDefaultAsync(m => m.MessageId == mensaje.Id && m.ColaOrigen == cola, ct);
            if (fila is null) db.MensajesFallidos.Add(MensajeFallido.Registrar(mensaje, cola, mensaje.Intento, error, ahora));
            else fila.VolvioAFallar(mensaje.Intento, error, ahora);
            await db.SaveChangesAsync(ct);
        }
        await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct);
        return true;
    }
}
