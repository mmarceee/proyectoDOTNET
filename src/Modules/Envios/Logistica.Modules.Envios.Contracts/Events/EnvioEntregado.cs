namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el envío se entregó (CU-17, T6). Lo esperan Seguimiento, para avisar
// al destinatario y al comercio, y las liquidaciones. La hora es la del dispositivo. ResponsableId es el usuario que
// registró la entrega; RepartidorId, el repartidor de la flota, para los indicadores por repartidor (CU-71). Opcional:
// agregarlo no cambia la versión (ADR-0003, sección 2.3).
public sealed record EnvioEntregado(Guid EnvioId, string Numero, DateTimeOffset EntregadoEn, Guid? ResponsableId,
    Guid? RepartidorId = null)
{
    public const string Tipo = "EnvioEntregado.v1";
}
