using System.Text.Json;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// Evento de integración pendiente de publicar (ADR-0003, secciones 2.1 y 2.6). Cada módulo tiene su tabla
// outbox_messages en su schema; se escribe con el mismo SaveChanges que el cambio que la origina.
// No implementa IOperadorOwned (ADR-0002, sección 2.4): el publicador la lee sin inquilino, y con el filtro
// "Tenant" no vería nada. OperadorId y ComercioId son metadatos del sobre: los toma ModuleDbContext del
// inquilino del request que generó el evento, nunca del llamador ni del contenido.
public sealed class OutboxMessage
{
    // MessageId del sobre: base de la idempotencia de los consumidores.
    public Guid Id { get; private set; }

    // Nombre versionado del evento (por ejemplo, "EnvioAsignadoARuta.v1"); también es la routing key.
    public string Tipo { get; private set; } = "";

    public DateTimeOffset OcurridoEn { get; private set; }

    // Con estos datos el consumidor fija su ICurrentTenant (ADR-0002, sección 2.5).
    public Guid OperadorId { get; private set; }
    public Guid? ComercioId { get; private set; }

    // Contexto de traza de OpenTelemetry, para seguir el evento de la API al Worker.
    public string? TraceParent { get; private set; }

    // Datos del evento, en JSON.
    public string Contenido { get; private set; } = "";

    // null mientras está pendiente; lo marca el publicador (ADR-0003, sección 2.3).
    public DateTimeOffset? PublicadoEn { get; private set; }

    private OutboxMessage()
    {
    }

    // Sólo lo crea ModuleDbContext.AgregarAlOutbox, que es quien pone el inquilino.
    internal static OutboxMessage Crear<T>(
        string tipo, T contenido, DateTimeOffset ocurridoEn, Guid operadorId, Guid? comercioId, string? traceParent)
    {
        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Tipo = tipo,
            OcurridoEn = ocurridoEn,
            OperadorId = operadorId,
            ComercioId = comercioId,
            TraceParent = traceParent,
            Contenido = JsonSerializer.Serialize(contenido),
        };
    }
}
