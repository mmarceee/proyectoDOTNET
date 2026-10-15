namespace Logistica.Modules.Ejecucion.Domain.Operaciones;

// Acceso a las operaciones sincronizadas, sólo las del inquilino actual. Lo implementa Infrastructure.
internal interface IOperacionRepository
{
    // La operación con ese OperacionId, si ya se recibió (idempotencia, ADR-0004, sección 2.2).
    Task<OperacionDispositivo?> ObtenerAsync(Guid operacionId, CancellationToken ct);

    void Agregar(OperacionDispositivo operacion);

    Task GuardarCambiosAsync(CancellationToken ct);
}
