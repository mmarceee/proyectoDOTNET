using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.SharedKernel;
using static Logistica.Modules.Planificacion.Application.Mapping.RutaResponseMapper;

namespace Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;

internal sealed class ConsultarRutasHandler(IRutaRepository rutas, IEnviosModuleApi envios, IAdministracionModuleApi administracion)
{
    public Task<NombresRecursosPlanificacion> RecursosAsignadosAsync(IReadOnlyList<RutaResponse> lista, CancellationToken ct)
        => administracion.ConsultarNombresRecursosAsync(lista.Select(r => r.RepartidorId).Where(id => id != Guid.Empty).Distinct().ToArray(),
            lista.Select(r => r.VehiculoId).Where(id => id != Guid.Empty).Distinct().ToArray(), ct);
    public async Task<IReadOnlyDictionary<Guid, string>> NumerosEnvioAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => (await envios.ObtenerParaPlanificacionAsync(ids, ct)).ToDictionary(e => e.Id, e => e.Numero);
    public async Task<IReadOnlyList<RutaResponse>> ListarAsync(DateOnly? fecha, CancellationToken ct)
        => await ListarAsync(fecha, null, ct);
    public async Task<IReadOnlyList<RutaResponse>> ListarAsync(DateOnly? fecha, string? estado, CancellationToken ct)
    {
        EstadoRuta? filtro = null;
        if (!string.IsNullOrWhiteSpace(estado))
        {
            // Sólo nombres del catálogo; no se aceptan valores numéricos del enum.
            if (!Enum.GetNames<EstadoRuta>().Contains(estado))
                throw new DomainException("El estado de la ruta no es válido.");
            filtro = Enum.Parse<EstadoRuta>(estado);
        }
        return (await rutas.ListarAsync(fecha, filtro, ct)).Select(r => Mapear(r)).ToList();
    }
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
    public async Task<IReadOnlyList<RetiroEnvioRutaResponse>> RetirosAsync(Guid id, int pagina, CancellationToken ct)
    {
        await ExigirRutaAsync(id, ct);
        if (pagina < 1) throw new DomainException("La página debe ser positiva.");
        return (await rutas.RetirosAsync(id, pagina, ct)).Select(r => new RetiroEnvioRutaResponse(r.Id, r.EnvioId,
            r.NumeroEnvio, r.OrdenAnterior, r.ParadasRestantes, r.RevisionRuta, r.ResponsableId, r.OcurridoEn)).ToList();
    }
    public async Task<IReadOnlyList<AnulacionDespachoResponse>> AnulacionesAsync(Guid id, int pagina, CancellationToken ct)
    {
        await ExigirRutaAsync(id, ct);
        if (pagina < 1) throw new DomainException("La página debe ser positiva.");
        return (await rutas.AnulacionesAsync(id, pagina, ct)).Select(a => new AnulacionDespachoResponse(a.Id,
            a.FechaRuta, a.DespachadaEn, a.RevisionAnterior, a.RevisionRuta, a.Motivo, a.ResponsableId, a.OcurridoEn)).ToArray();
    }
    private async Task<Ruta> ExigirRutaAsync(Guid id, CancellationToken ct)
        => await rutas.ObtenerAsync(id, ct) ?? throw new RutaNoEncontradaException();
}
