namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): un bulto no se entregó en una visita (RF 31) y quedó Reprogramado o
// EnDevolucion, según EstadoBulto. La visita lo publica por cada bulto no entregado, además de IntentoFallidoRegistrado.
// Lo esperan los avisos v2 al comercio (RF 36).
public sealed record BultoNoEntregado(Guid EnvioId, string Numero, string CodigoBulto, string Motivo, string EstadoBulto,
    string EstadoEnvio, DateTimeOffset OcurridoEn, Guid? RepartidorId = null)
{
    public const string Tipo = "BultoNoEntregado.v1";
}
