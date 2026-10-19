namespace Logistica.Modules.Administracion.Domain.Planificacion;

// Acceso a las zonas y sus franjas (CU-04). Lo implementa Infrastructure, así Application no depende de EF Core.
// No recibe el operador: el filtro "Tenant" ya devuelve sólo las del operador de la sesión (ADR-0002).
internal interface IZonaRepository
{
    Task<IReadOnlyList<Zona>> ListarAsync(CancellationToken ct);

    Task<Zona?> ObtenerAsync(Guid id, CancellationToken ct);

    // A1: el código es único por operador.
    Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct);

    // A3: las franjas de la zona, para comprobar que una nueva no se superponga.
    Task<IReadOnlyList<FranjaHoraria>> FranjasDeAsync(Guid zonaId, CancellationToken ct);

    void Agregar(Zona zona);

    void AgregarFranja(FranjaHoraria franja);

    Task GuardarCambiosAsync(CancellationToken ct);
}
