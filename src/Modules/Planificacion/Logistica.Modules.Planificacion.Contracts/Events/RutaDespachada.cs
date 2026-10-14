namespace Logistica.Modules.Planificacion.Contracts.Events;

public sealed record RutaDespachada(Guid RutaId, Guid RepartidorId, DateOnly Fecha, long Revision, Guid? ResponsableId)
{
    public const string Tipo = "RutaDespachada.v1";
}
