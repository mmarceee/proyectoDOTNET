namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal interface IRutaRepository
{
    Task<Ruta?> ObtenerAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Ruta>> ListarAsync(DateOnly? fecha, EstadoRuta? estado, CancellationToken ct);
    // CU-04: ¿alguno de estos envíos está en una ruta Planificada, Despachada o EnCurso desde esa fecha?
    Task<bool> HayActivasConEnviosAsync(IReadOnlyCollection<Guid> envioIds, DateOnly desde, CancellationToken ct);
    Task<IReadOnlyList<Guid>> EnviosOcupadosAsync(IReadOnlyCollection<Guid> ids, Guid? rutaPropia, CancellationToken ct);
    Task<IReadOnlyList<string>> RecursosOcupadosAsync(DateOnly fecha, Guid repartidor, Guid vehiculo, Guid? rutaPropia, CancellationToken ct);
    Task<IReadOnlyList<ValidacionRuta>> ValidacionesAsync(Guid rutaId, int pagina, CancellationToken ct);
    Task<IReadOnlyList<RetiroEnvioRuta>> RetirosAsync(Guid rutaId, int pagina, CancellationToken ct);
    void Agregar(Ruta ruta);
    void Agregar(ValidacionRuta validacion);
    void Agregar(RetiroEnvioRuta retiro);
    Task GuardarAsync(CancellationToken ct);
}
