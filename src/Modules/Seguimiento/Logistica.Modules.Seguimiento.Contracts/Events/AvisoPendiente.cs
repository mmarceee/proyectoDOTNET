namespace Logistica.Modules.Seguimiento.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.3): hay una entrega de aviso lista para enviar al comercio (CU-64).
// Una por suscripción, así cada una se reintenta sola. No lleva el secreto: se lee al preparar cada intento.
public sealed record AvisoPendiente(Guid EntregaId)
{
    public const string Tipo = "AvisoPendiente.v1";
}
