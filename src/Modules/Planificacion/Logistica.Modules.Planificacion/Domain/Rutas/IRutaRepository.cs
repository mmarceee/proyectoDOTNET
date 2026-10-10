namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal interface IRutaRepository
{
    Task<Ruta?> ObtenerAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Ruta>> ListarAsync(DateOnly? fecha, CancellationToken ct);
    Task<IReadOnlyList<Guid>> EnviosOcupadosAsync(IReadOnlyCollection<Guid> ids, Guid? rutaPropia, CancellationToken ct);
    Task<IReadOnlyList<string>> RecursosOcupadosAsync(DateOnly fecha, Guid repartidor, Guid vehiculo, Guid? rutaPropia, CancellationToken ct);
    Task<IReadOnlyList<ValidacionRuta>> ValidacionesAsync(Guid rutaId, int pagina, CancellationToken ct);
    void Agregar(Ruta ruta);
    void Agregar(ValidacionRuta validacion);
    Task GuardarAsync(CancellationToken ct);
}
