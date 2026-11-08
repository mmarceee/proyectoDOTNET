using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Deposito.Contracts.Results;
using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Application;

internal sealed class DepositoModuleApi(IRecepcionRepository recepciones, ILiquidacionRepository liquidaciones,
    Features.GenerarLiquidacion.GenerarLiquidacionHandler generar) : IDepositoModuleApi
{
    public Task<int> GenerarLiquidacionesDelPeriodoAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
        => generar.GenerarDelPeriodoAsync(desde, hasta, ct);

    public async Task<IReadOnlyList<MedidasBultoRecibido>> ObtenerMedidasRecepcionAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
        => (await recepciones.ConsultarPorBultosAsync(bultoIds, ct)).Select(r => new MedidasBultoRecibido(r.BultoId, r.PesoKg, r.LargoCm, r.AnchoCm, r.AltoCm, r.RecibidoEn)).ToList();

    public async Task<IReadOnlyList<Guid>> BultosRecibidosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
        => (await recepciones.ConsultarPorBultosAsync(bultoIds, ct)).Select(r => r.BultoId).ToList();

    public async Task<IReadOnlyList<ResumenLiquidacionesComercio>> ResumirLiquidacionesAsync(DateOnly desde, DateOnly hasta,
        CancellationToken ct)
    {
        if (hasta < desde)
            throw new DomainException("La fecha desde es posterior a la fecha hasta.");
        return (await liquidaciones.ListarEmitidasEnPeriodoAsync(desde, hasta, ct))
            .GroupBy(l => l.ComercioId)
            .Select(g => new ResumenLiquidacionesComercio(g.Key, g.Count(), g.Sum(l => l.Lineas.Count), g.Sum(l => l.TotalNeto),
                g.Where(l => l.Estado == EstadoLiquidacion.Pagada).Sum(l => l.TotalNeto),
                g.Where(l => l.Estado == EstadoLiquidacion.Emitida).Sum(l => l.TotalNeto)))
            .ToList();
    }
}
