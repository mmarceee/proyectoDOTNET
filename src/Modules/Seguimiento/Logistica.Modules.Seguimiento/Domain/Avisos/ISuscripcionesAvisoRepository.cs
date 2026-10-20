namespace Logistica.Modules.Seguimiento.Domain.Avisos;

internal interface ISuscripcionesAvisoRepository
{
    void Agregar(SuscripcionAviso suscripcion);
    Task<SuscripcionAviso?> ObtenerAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<SuscripcionAviso>> ListarAsync(CancellationToken ct);
    Task GuardarAsync(CancellationToken ct);
}
