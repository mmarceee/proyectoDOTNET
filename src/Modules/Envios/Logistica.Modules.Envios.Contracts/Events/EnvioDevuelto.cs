namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el comercio recibió el envío devuelto (CU-31, T15). Lo esperan
// Seguimiento, para avisar al comercio (CU-64) y actualizar el seguimiento público (CU-60), y las liquidaciones (CU-32).
// Identificador, inquilino, fecha y traza pertenecen al sobre del Outbox.
public sealed record EnvioDevuelto(Guid EnvioId, string Numero, DateTimeOffset OcurridoEn, Guid? ResponsableId)
{
    public const string Tipo = "EnvioDevuelto.v1";
}
