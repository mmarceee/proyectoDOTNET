namespace Logistica.Modules.Administracion.Application.Features.SeleccionarOperador;

// Los operadores con los que un comercio tiene una relación activa (CU-03). Lo implementa Infrastructure con la única
// consulta autorizada a ignorar el filtro "Tenant" para listar operadores (ADR-0002, sección 2.4).
internal interface IOperadoresDelComercio
{
    Task<IReadOnlyList<OperadorDisponible>> ActivosAsync(Guid comercioId, CancellationToken ct);
}

internal sealed record OperadorDisponible(Guid Id, string Nombre);
