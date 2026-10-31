namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el depósito recibió el envío (CU-30, T2). Lo esperan Seguimiento, para
// avisar al comercio (CU-64) y actualizar el seguimiento público (CU-60). Identificador, inquilino, fecha y traza
// pertenecen al sobre del Outbox.
public sealed record EnvioRecibidoEnDeposito(Guid EnvioId, string Numero, DateTimeOffset OcurridoEn, Guid? ResponsableId)
{
    public const string Tipo = "EnvioRecibidoEnDeposito.v1";
}
