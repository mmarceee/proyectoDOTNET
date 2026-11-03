namespace Logistica.Modules.Envios.Domain.Envios;

// El resultado de un bulto en una visita (RF 31): sin motivo, se entregó; con motivo, no se pudo entregar.
internal sealed record ResultadoBulto(Guid BultoId, MotivoNoEntrega? Motivo = null);
