namespace Logistica.Modules.Ejecucion.Domain.Rendiciones;

internal sealed record EvidenciaRuta(IReadOnlySet<Guid> BultosEscaneados, IReadOnlySet<Guid> Intentados,
    IReadOnlyList<OperacionRuta> Operaciones);
internal sealed record OperacionRuta(Guid Id, Guid RepartidorId, Guid? EnvioId, string Tipo,
    Operaciones.ResultadoOperacion Resultado, bool Revisada);

internal interface IRendicionRepository
{
    Task<Rendicion?> ObtenerAsync(Guid rutaId, CancellationToken ct);
    Task<IReadOnlyList<Rendicion>> PendientesAsync(int pagina, CancellationToken ct);
    Task<EvidenciaRuta> EvidenciaAsync(Guid rutaId, CancellationToken ct);
    void Agregar(Rendicion rendicion);
    Task GuardarAsync(CancellationToken ct);
}
