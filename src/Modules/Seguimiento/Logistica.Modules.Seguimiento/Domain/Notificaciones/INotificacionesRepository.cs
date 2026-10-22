namespace Logistica.Modules.Seguimiento.Domain.Notificaciones;

internal interface INotificacionesRepository
{
    void Agregar(Notificacion notificacion);
    Task<Notificacion?> ObtenerPorEventoAsync(Guid eventoId, CancellationToken ct);
    Task GuardarAsync(CancellationToken ct);
}

internal interface IIdentidadesOperadorRepository
{
    void Agregar(IdentidadOperador identidad);
    Task<IdentidadOperador?> ObtenerAsync(Guid operadorId, CancellationToken ct);
    Task GuardarAsync(CancellationToken ct);
}
