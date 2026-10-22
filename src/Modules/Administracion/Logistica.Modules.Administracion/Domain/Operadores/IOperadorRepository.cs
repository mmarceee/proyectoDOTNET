namespace Logistica.Modules.Administracion.Domain.Operadores;

// Acceso a los operadores y a sus logos. Lo implementa Infrastructure, así Application no depende de EF Core.
internal interface IOperadorRepository
{
    Task<IReadOnlyList<Guid>> ConsultarActivosAsync(CancellationToken ct);
    // Operador no tiene filtro de inquilino: quien llama pasa el operador de la sesión, nunca uno que mande el cliente.
    Task<Operador?> ObtenerAsync(Guid id, CancellationToken ct);

    // Los logos sí tienen filtro: sólo se encuentran los del operador de la sesión.
    Task<LogoOperador?> ObtenerLogoAsync(Guid id, CancellationToken ct);

    void AgregarLogo(LogoOperador logo);

    Task GuardarCambiosAsync(CancellationToken ct);
}
