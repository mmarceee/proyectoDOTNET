namespace Logistica.Modules.Seguimiento.Domain.Reprogramaciones;

internal interface IVerificacionesReprogramacionRepository
{
    void Agregar(VerificacionReprogramacion verificacion);
    // Las que todavía pueden usarse (ni usadas ni anuladas), para anularlas al pedir una nueva.
    Task<IReadOnlyList<VerificacionReprogramacion>> ListarAbiertasAsync(Guid envioId, CancellationToken ct);
    // Cuántas se pidieron para el envío desde "desde", para limitar los pedidos.
    Task<int> ContarDesdeAsync(Guid envioId, DateTimeOffset desde, CancellationToken ct);
    // La última pedida para el envío: es la única que vale.
    Task<VerificacionReprogramacion?> UltimaAsync(Guid envioId, CancellationToken ct);
    Task GuardarAsync(CancellationToken ct);
}
