namespace Logistica.Modules.Planificacion.Contracts.Events;

public sealed record DespachoAnulado(Guid RutaId, Guid RepartidorId, DateOnly Fecha, long Revision, Guid? ResponsableId)
{
    public const string Tipo = "DespachoAnulado.v1";
}
