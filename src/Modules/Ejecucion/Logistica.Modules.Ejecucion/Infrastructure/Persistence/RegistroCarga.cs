using Logistica.Modules.Ejecucion.Application.Features.ConfirmarCarga;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Ejecucion.Domain.Cargas;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence;

internal sealed class RegistroCarga(EjecucionDbContext db) : IRegistroCarga
{
    public async Task GuardarAsync(IReadOnlyList<EscaneoCarga> escaneos, CargaConDiscrepancias? aviso, DateTimeOffset ocurridoEn, CancellationToken ct)
    {
        db.EscaneosCarga.AddRange(escaneos);
        if (aviso is not null) db.AgregarAlOutbox(CargaConDiscrepancias.Tipo, aviso, ocurridoEn);
        await db.SaveChangesAsync(ct);
    }
}
