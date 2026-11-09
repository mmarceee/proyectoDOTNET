namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el comercio recibió el envío devuelto (CU-31, T15). Lo esperan
// Seguimiento, para avisar al comercio (CU-64) y actualizar el seguimiento público (CU-60), y las liquidaciones (CU-32).
// Identificador, inquilino, fecha y traza pertenecen al sobre del Outbox. BultosEntregados (RF 36): los bultos que el
// destinatario ya había recibido; con alguno, el cierre es mixto y los avisos v1 no lo reciben. Opcional: agregarlo
// no cambia la versión (ADR-0003, sección 2.3).
public sealed record EnvioDevuelto(Guid EnvioId, string Numero, DateTimeOffset OcurridoEn, Guid? ResponsableId,
    int BultosEntregados = 0)
{
    public const string Tipo = "EnvioDevuelto.v1";
}
