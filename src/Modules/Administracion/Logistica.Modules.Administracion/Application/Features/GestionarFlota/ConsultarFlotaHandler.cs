using Logistica.Modules.Administracion.Domain.Planificacion;

namespace Logistica.Modules.Administracion.Application.Features.GestionarFlota;

internal sealed record RepartidorDto(Guid Id, string Nombre, string Documento, bool TieneUsuario, bool Activo);

internal sealed record VehiculoDto(Guid Id, string Matricula, string Tipo, decimal CapacidadPesoKg,
    decimal CapacidadVolumenM3, decimal LargoCargaCm, decimal AnchoCargaCm, decimal AltoCargaCm, bool Activo);

// CU-08, consulta: los repartidores y vehículos del operador, activos e inactivos. Sólo lee: el filtro "Tenant" deja
// ver únicamente los del operador de la sesión.
internal sealed class ConsultarFlotaHandler(IFlotaRepository flota)
{
    public async Task<IReadOnlyList<RepartidorDto>> RepartidoresAsync(CancellationToken ct)
    {
        return (await flota.ListarRepartidoresAsync(ct)).OrderByDescending(r => r.Activo).ThenBy(r => r.Nombre)
            .Select(Mapear).ToList();
    }

    public async Task<IReadOnlyList<VehiculoDto>> VehiculosAsync(CancellationToken ct)
    {
        return (await flota.ListarVehiculosAsync(ct)).OrderByDescending(v => v.Activo).ThenBy(v => v.Matricula)
            .Select(Mapear).ToList();
    }

    // Uno de otro operador no se encuentra: null, y la página responde 404 (ADR-0002, sección 2.9).
    public async Task<RepartidorDto?> RepartidorAsync(Guid id, CancellationToken ct)
    {
        return await flota.ObtenerRepartidorAsync(id, ct) is Repartidor r ? Mapear(r) : null;
    }

    public async Task<VehiculoDto?> VehiculoAsync(Guid id, CancellationToken ct)
    {
        return await flota.ObtenerVehiculoAsync(id, ct) is Vehiculo v ? Mapear(v) : null;
    }

    private static RepartidorDto Mapear(Repartidor r) => new(r.Id, r.Nombre, r.Documento, r.UsuarioId is not null, r.Activo);

    private static VehiculoDto Mapear(Vehiculo v) => new(v.Id, v.Matricula, v.Tipo, v.CapacidadPesoKg, v.CapacidadVolumenM3,
        v.LargoCargaCm, v.AnchoCargaCm, v.AltoCargaCm, v.Activo);
}
