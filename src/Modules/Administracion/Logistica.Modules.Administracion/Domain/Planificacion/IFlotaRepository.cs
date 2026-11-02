namespace Logistica.Modules.Administracion.Domain.Planificacion;

// Acceso a los repartidores y vehículos (CU-08). Lo implementa Infrastructure, así Application no depende de EF Core.
// No recibe el operador: el filtro "Tenant" ya devuelve sólo los del operador de la sesión (ADR-0002).
internal interface IFlotaRepository
{
    Task<IReadOnlyList<Repartidor>> ListarRepartidoresAsync(CancellationToken ct);

    Task<IReadOnlyList<Vehiculo>> ListarVehiculosAsync(CancellationToken ct);

    Task<Repartidor?> ObtenerRepartidorAsync(Guid id, CancellationToken ct);

    Task<Vehiculo?> ObtenerVehiculoAsync(Guid id, CancellationToken ct);

    // A1: el documento y la matrícula son únicos por operador.
    Task<bool> ExisteDocumentoAsync(string documento, CancellationToken ct);

    Task<bool> ExisteMatriculaAsync(string matricula, CancellationToken ct);

    void Agregar(Repartidor repartidor);

    void Agregar(Vehiculo vehiculo);

    Task GuardarCambiosAsync(CancellationToken ct);
}
