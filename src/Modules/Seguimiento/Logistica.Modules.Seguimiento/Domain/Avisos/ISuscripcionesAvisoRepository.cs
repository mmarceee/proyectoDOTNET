namespace Logistica.Modules.Seguimiento.Domain.Avisos;

internal interface ISuscripcionesAvisoRepository
{
    void Agregar(SuscripcionAviso suscripcion);
    Task<SuscripcionAviso?> ObtenerAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<SuscripcionAviso>> ListarAsync(CancellationToken ct);
    // CU-64: las suscripciones activas del comercio de un envío, dentro del operador del mensaje.
    Task<IReadOnlyList<SuscripcionAviso>> ListarActivasAsync(Guid comercioId, CancellationToken ct);
    Task GuardarAsync(CancellationToken ct);
}
