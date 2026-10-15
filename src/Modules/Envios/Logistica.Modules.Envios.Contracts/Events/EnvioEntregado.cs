namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el envío se entregó (CU-17, T6). Lo esperan Seguimiento, para avisar
// al destinatario y al comercio, y las liquidaciones. La hora es la del dispositivo.
public sealed record EnvioEntregado(Guid EnvioId, string Numero, DateTimeOffset EntregadoEn, Guid? ResponsableId)
{
    public const string Tipo = "EnvioEntregado.v1";
}
