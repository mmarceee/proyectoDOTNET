using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.ConfirmarRendicion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Deposito.Presentation.Features.ConfirmarRendicion;

// CU-55, paso 1: las rendiciones que los repartidores iniciaron (CU-54) y el depósito todavía no confirmó.
[Authorize(Policy = Politicas.OperarioDeposito)]
internal sealed class IndexModel(ConfirmarRendicionHandler handler) : PageModel
{
    public IReadOnlyList<RendicionEnListado> Rendiciones { get; private set; } = [];
    public string? Mensaje { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "pendientes";

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public bool Confirmadas => Estado == "confirmadas";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Estado is not ("pendientes" or "confirmadas") || Pagina is < 1 or > 10000) return BadRequest();
        Mensaje = TempData["Mensaje"] as string;
        Rendiciones = await handler.ListarAsync(Pagina, Confirmadas, ct);
        return Page();
    }
}
