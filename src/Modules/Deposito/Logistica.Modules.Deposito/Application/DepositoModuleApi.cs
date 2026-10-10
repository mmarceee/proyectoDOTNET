using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Deposito.Contracts.Results;
using Logistica.Modules.Deposito.Domain.Recepciones;

namespace Logistica.Modules.Deposito.Application;

internal sealed class DepositoModuleApi(IRecepcionRepository recepciones) : IDepositoModuleApi
{
    public async Task<IReadOnlyList<MedidasBultoRecibido>> ObtenerMedidasRecepcionAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
        => (await recepciones.ConsultarPorBultosAsync(bultoIds, ct)).Select(r => new MedidasBultoRecibido(r.BultoId, r.PesoKg, r.LargoCm, r.AnchoCm, r.AltoCm, r.RecibidoEn)).ToList();

    public async Task<IReadOnlyList<Guid>> BultosRecibidosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
        => (await recepciones.ConsultarPorBultosAsync(bultoIds, ct)).Select(r => r.BultoId).ToList();
}
