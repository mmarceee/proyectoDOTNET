using System.Text.Json;
using Logistica.SharedKernel;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// Registro durable por módulo. La entrega/publicación corresponde al ADR-0003, no al SaveChanges.
public sealed class OutboxMessage : IOperadorOwned
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }
    public string Tipo { get; private set; } = "";
    public string Contenido { get; private set; } = "";
    public DateTimeOffset? PublicadoEn { get; private set; }
    private OutboxMessage() { }
    public static OutboxMessage Crear<T>(Guid operador, string tipo, T contenido, DateTimeOffset ahora, Guid? id = null)
        => new() { Id = id ?? Guid.CreateVersion7(), OperadorId = operador, Tipo = tipo,
            Contenido = JsonSerializer.Serialize(contenido), OcurridoEn = ahora };
}
