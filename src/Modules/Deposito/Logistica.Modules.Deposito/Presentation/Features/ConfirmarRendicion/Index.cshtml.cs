using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.ConfirmarRendicion;
using Logistica.Modules.Ejecucion.Contracts.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Deposito.Presentation.Features.ConfirmarRendicion;

// CU-55, paso 1: las rendiciones que los repartidores iniciaron (CU-54) y el depósito todavía no confirmó.
[Authorize(Policy = Politicas.OperarioDeposito)]
internal sealed class IndexModel(ConfirmarRendicionHandler handler) : PageModel
{
    public IReadOnlyList<RendicionPendiente> Rendiciones { get; private set; } = [];
    public string? Mensaje { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        Rendiciones = await handler.PendientesAsync(1, ct);
    }
}
