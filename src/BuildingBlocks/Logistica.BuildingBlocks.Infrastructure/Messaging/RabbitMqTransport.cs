using System.Text;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging;

public static class RabbitMqTransport
{
    public const string Exchange = "logistica.events";
    public const string Fallidos = "logistica.fallidos";
    public static readonly int[] Esperas = [5_000, 30_000, 120_000];

    public static ConnectionFactory CrearFactory(IConfiguration configuration) => new()
    {
        Uri = new Uri(configuration.GetConnectionString("RabbitMQ")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:RabbitMQ.")),
        AutomaticRecoveryEnabled = false,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
        ClientProvidedName = "logistica",
    };

    public static async Task DeclararAsync(IChannel canal, IEnumerable<SuscripcionIntegracion> suscripciones, CancellationToken ct)
    {
        await canal.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await canal.QueueDeclareAsync(Fallidos, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        foreach (var s in suscripciones)
        {
            await canal.QueueDeclareAsync(s.Cola, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
            await canal.QueueBindAsync(s.Cola, Exchange, s.Tipo, cancellationToken: ct);
            for (var i = 0; i < Esperas.Length; i++)
                await canal.QueueDeclareAsync(ColaEspera(s.Cola, i), durable: true, exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-message-ttl"] = Esperas[i], ["x-dead-letter-exchange"] = "",
                        ["x-dead-letter-routing-key"] = s.Cola,
                    }, cancellationToken: ct);
        }
    }

    public static string ColaEspera(string cola, int intento) => cola + new[] { ".espera.5s", ".espera.30s", ".espera.2m" }[intento];

    public static BasicProperties Propiedades(MensajeIntegracion mensaje) => new()
    {
        MessageId = mensaje.Id.ToString(), Type = mensaje.Tipo, ContentType = "application/json", Persistent = true,
        Timestamp = new AmqpTimestamp(mensaje.OcurridoEn.ToUnixTimeSeconds()),
        Headers = new Dictionary<string, object?>
        {
            ["operador-id"] = mensaje.OperadorId.ToString(), ["comercio-id"] = mensaje.ComercioId?.ToString(),
            ["traceparent"] = mensaje.TraceParent,
        },
    };

    public static MensajeIntegracion Leer(IReadOnlyBasicProperties propiedades, ReadOnlyMemory<byte> cuerpo)
    {
        if (!Guid.TryParse(propiedades.MessageId, out var id) || id == Guid.Empty
            || !Guid.TryParse(Cabecera(propiedades, "operador-id"), out var operador) || operador == Guid.Empty
            || string.IsNullOrWhiteSpace(propiedades.Type))
            throw new InvalidDataException("Sobre sin MessageId, tipo u operador válido.");
        Guid? comercio = null;
        var valor = Cabecera(propiedades, "comercio-id");
        if (!string.IsNullOrEmpty(valor))
            comercio = Guid.TryParse(valor, out var guid) && guid != Guid.Empty ? guid
                : throw new InvalidDataException("Comercio inválido en el sobre.");
        return new(id, propiedades.Type, operador, comercio, DateTimeOffset.FromUnixTimeSeconds(propiedades.Timestamp.UnixTime),
            Cabecera(propiedades, "traceparent"), Encoding.UTF8.GetString(cuerpo.Span));
    }

    internal static string? Cabecera(IReadOnlyBasicProperties p, string nombre)
        => p.Headers?.TryGetValue(nombre, out var valor) == true
            ? valor is byte[] bytes ? Encoding.UTF8.GetString(bytes) : valor?.ToString() : null;
}
