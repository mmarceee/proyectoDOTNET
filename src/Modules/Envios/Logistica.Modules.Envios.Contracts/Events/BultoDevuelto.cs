namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el comercio recibió un bulto devuelto (CU-31). Se publica por cada
// bulto que el operario entregó al comercio, además de EnvioDevuelto. Lo esperan los avisos v2 al comercio (RF 36).
public sealed record BultoDevuelto(Guid EnvioId, string Numero, string CodigoBulto, string EstadoEnvio,
    DateTimeOffset DevueltoEn)
{
    public const string Tipo = "BultoDevuelto.v1";
}
