using Logistica.Modules.Planificacion.Contracts.Results;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application;

internal sealed class ReservasBultos(IReservaBultoRepository reservas, TimeProvider reloj)
{
    public async Task<IReadOnlyList<ReservaBultoRuta>> DeRutaAsync(Guid rutaId, CancellationToken ct)
        => (await reservas.DeRutaAsync(rutaId, ct)).Select(r => new ReservaBultoRuta(r.BultoId, r.EnvioId,
            r.RutaId, r.ParadaId, r.Revision, r.CerradaEn, r.CausaCierre, r.AbiertaEn)).ToArray();

    public Task<IReadOnlyList<Guid>> OcupadosAsync(IReadOnlyCollection<Guid> bultos, Guid? propia, CancellationToken ct)
        => reservas.OcupadosAsync(bultos, propia, ct);
    public async Task<IReadOnlyList<ReservaBultoRuta>> ActivasAsync(IReadOnlyCollection<Guid> bultos, CancellationToken ct)
        => (await reservas.ActivasAsync(bultos, ct)).Select(r => new ReservaBultoRuta(r.BultoId, r.EnvioId,
            r.RutaId, r.ParadaId, r.Revision, r.CerradaEn, r.CausaCierre, r.AbiertaEn)).ToArray();

    public async Task CerrarAsync(Guid rutaId, IReadOnlyCollection<Guid> bultos, string causa, CancellationToken ct)
    {
        if (bultos.Count == 0) return;
        var abiertas = (await reservas.DeRutaAsync(rutaId, ct))
            .Where(r => bultos.Contains(r.BultoId) && r.CerradaEn == null).ToArray();
        if (bultos.Distinct().Count() != bultos.Count || abiertas.Length != bultos.Count)
            throw new DomainException("Los bultos no tienen una reserva abierta en esta ruta.");
        foreach (var reserva in abiertas) reserva.Cerrar(causa, reloj.GetUtcNow());
        await reservas.GuardarAsync(ct);
    }
}
