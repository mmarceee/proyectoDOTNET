namespace Logistica.Modules.Envios.Contracts.Events;

// Hecho interno para invalidación durable. No amplía el contrato de avisos externos.
public sealed record PlanificacionBultosActualizada(Guid EnvioId, Guid RutaId, IReadOnlyList<Guid> BultoIds,
    string Accion, DateTimeOffset OcurridoEn)
{
    public const string Tipo = "PlanificacionBultosActualizada.v1";
}
