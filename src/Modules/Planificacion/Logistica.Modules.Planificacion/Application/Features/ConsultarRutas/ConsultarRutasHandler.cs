using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.SharedKernel;
using static Logistica.Modules.Planificacion.Application.Mapping.RutaResponseMapper;

namespace Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;

internal sealed class ConsultarRutasHandler(IRutaRepository rutas, IEnviosModuleApi envios)
{
    public async Task<IReadOnlyDictionary<Guid, string>> NumerosEnvioAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => (await envios.ObtenerParaPlanificacionAsync(ids, ct)).ToDictionary(e => e.Id, e => e.Numero);
    public async Task<IReadOnlyList<RutaResponse>> ListarAsync(DateOnly? fecha, CancellationToken ct)
        => (await rutas.ListarAsync(fecha, ct)).Select(r => Mapear(r)).ToList();
    public async Task<RutaResponse> DetalleAsync(Guid id, CancellationToken ct) => Mapear(await ExigirRutaAsync(id, ct));
    public async Task<IReadOnlyList<ValidacionRutaResponse>> ValidacionesAsync(Guid id, int pagina, CancellationToken ct)
    {
        await ExigirRutaAsync(id, ct);
        if (pagina < 1) throw new DomainException("La página debe ser positiva.");
        return (await rutas.ValidacionesAsync(id, pagina, ct)).Select(v => new ValidacionRutaResponse(v.Id, v.RevisionRuta,
            v.ValidadaEn, v.ResponsableId, v.Operacion.ToString(), v.FechaRuta, v.RepartidorId, v.VehiculoId, v.VersionReglasId,
            v.MaxParadasPorRuta, v.CapacidadPesoKg, v.CapacidadVolumenM3, v.LargoCargaCm, v.AnchoCargaCm, v.AltoCargaCm,
            v.Bultos.Select(b => new BultoValidadoResponse(b.EnvioId, b.BultoId, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm, b.VolumenM3)).ToList())).ToList();
    }
    private async Task<Ruta> ExigirRutaAsync(Guid id, CancellationToken ct)
        => await rutas.ObtenerAsync(id, ct) ?? throw new RutaNoEncontradaException();
}
