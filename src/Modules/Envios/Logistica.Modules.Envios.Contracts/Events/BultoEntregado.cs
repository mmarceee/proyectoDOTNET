namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): un bulto se entregó en una visita (RF 31). La visita lo publica por
// cada bulto entregado, además de los eventos del envío, que no cambian para los consumidores actuales. Lo esperan
// los avisos v2 al comercio (RF 36). EstadoEnvio es el estado de compatibilidad en que quedó el envío después de la
// visita; el código del bulto es el de su etiqueta.
public sealed record BultoEntregado(Guid EnvioId, string Numero, string CodigoBulto, string EstadoEnvio,
    DateTimeOffset EntregadoEn, Guid? RepartidorId = null)
{
    public const string Tipo = "BultoEntregado.v1";
}
