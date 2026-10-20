namespace Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;

// Sólo lo devuelven creación y regeneración después de guardar. No usar como resultado de consulta.
// Clase sin ToString generado que incluya el secreto.
internal sealed class SecretoSuscripcionAviso(Guid id, string secretoFirma)
{
    public Guid Id { get; } = id;
    public string SecretoFirma { get; } = secretoFirma;
}
