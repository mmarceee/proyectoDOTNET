namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal interface IReservaBultoRepository
{
    Task<IReadOnlyList<ReservaBulto>> DeRutaAsync(Guid rutaId, CancellationToken ct);
    Task<IReadOnlyList<ReservaBulto>> ActivasAsync(IReadOnlyCollection<Guid> bultos, CancellationToken ct);
    Task<IReadOnlyList<ReservaBulto>> DelEnvioAsync(Guid envioId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> OcupadosAsync(IReadOnlyCollection<Guid> bultos, Guid? propia, CancellationToken ct);
    void Agregar(ReservaBulto reserva);
    Task GuardarAsync(CancellationToken ct);
}
