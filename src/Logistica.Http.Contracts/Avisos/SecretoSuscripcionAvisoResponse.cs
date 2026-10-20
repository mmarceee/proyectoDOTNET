namespace Logistica.Http.Contracts.Avisos;

// Sólo creación y regeneración: sin ToString generado que incluya el secreto.
public sealed class SecretoSuscripcionAvisoResponse(Guid id, string secretoFirma)
{
    public Guid Id { get; } = id;
    public string SecretoFirma { get; } = secretoFirma;
}
