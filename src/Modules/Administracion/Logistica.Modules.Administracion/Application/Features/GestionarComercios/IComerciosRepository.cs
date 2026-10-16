using Logistica.Modules.Administracion.Domain.Comercios;

namespace Logistica.Modules.Administracion.Application.Features.GestionarComercios;

internal sealed record ComercioVista
{
    public Guid Id { get; init; }
    public string RazonSocial { get; init; } = "";
    public string DocumentoFiscal { get; init; } = "";
    public string EmailContacto { get; init; } = "";
    public EstadoRelacion Estado { get; init; }
    public DateTimeOffset FechaAlta { get; init; }
    public uint Version { get; init; }
}

internal interface IComerciosRepository
{
    Task<IReadOnlyList<ComercioVista>> ListarAsync(string? buscar, EstadoRelacion? estado, CancellationToken ct);
    Task<ComercioVista?> ObtenerAsync(Guid comercioId, CancellationToken ct);
    // Excepción acotada de CU-01 A1: búsqueda exacta del documento global, sin datos de otras relaciones.
    Task<Comercio?> BuscarDocumentoAsync(string documento, CancellationToken ct);
    Task<RelacionComercial?> BloquearRelacionAsync(Guid comercioId, CancellationToken ct);
    void Agregar(Comercio comercio);
    void Agregar(RelacionComercial relacion);
    Task GuardarAsync(CancellationToken ct);
}
