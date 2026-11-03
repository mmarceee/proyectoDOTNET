namespace Logistica.Modules.Deposito.Domain.Liquidaciones;

// Sólo ve las liquidaciones del operador de la sesión (filtro "Tenant", ADR-0002).
internal interface ILiquidacionRepository
{
    // A2: de estos envíos, los que ya están en alguna liquidación.
    Task<IReadOnlySet<Guid>> EnviosLiquidadosAsync(IReadOnlyCollection<Guid> envioIds, CancellationToken ct);

    Task<IReadOnlyList<Liquidacion>> ListarAsync(CancellationToken ct);

    // Con sus líneas.
    Task<Liquidacion?> ObtenerAsync(Guid id, CancellationToken ct);

    void Agregar(Liquidacion liquidacion);

    // Con sus líneas (borrado en cascada).
    void Eliminar(Liquidacion liquidacion);

    // Si otra liquidación incluyó el mismo envío a la vez (A2), el índice único lo rechaza y lanza DomainException.
    Task GuardarCambiosAsync(CancellationToken ct);
}
