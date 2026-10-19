using Logistica.Modules.Ejecucion.Domain.Cargas;
using Logistica.Modules.Ejecucion.Contracts.Events;

namespace Logistica.Modules.Ejecucion.Application.Features.ConfirmarCarga;

internal interface IRegistroCarga
{
    Task GuardarAsync(IReadOnlyList<EscaneoCarga> escaneos, CargaConDiscrepancias? aviso, DateTimeOffset ocurridoEn, CancellationToken ct);
}
