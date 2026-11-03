using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.GenerarLiquidacion;
using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Deposito.Presentation.Features.GenerarLiquidacion;

// CU-32, paso 6 y A3: el administrador revisa la liquidación, la emite y después registra el pago; un borrador también
// se puede eliminar. Una liquidación de otro operador no se encuentra (ADR-0002).
[Authorize(Policy = Politicas.Administrador)]
internal sealed class DetalleModel(GenerarLiquidacionHandler handler) : PageModel
{
    public Liquidacion Liquidacion { get; private set; } = null!;
    public string Comercio { get; private set; } = "";
    public string? Mensaje { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (await handler.ObtenerAsync(id, ct) is not { } liquidacion)
            return NotFound();
        Liquidacion = liquidacion;
        Comercio = (await handler.ComerciosAsync(ct)).FirstOrDefault(c => c.ComercioId == liquidacion.ComercioId)?.RazonSocial ?? "—";
        Mensaje = TempData["Mensaje"] as string;
        if (TempData["Error"] is string error) ModelState.AddModelError(string.Empty, error);
        return Page();
    }

    public Task<IActionResult> OnPostEmitirAsync(Guid id, CancellationToken ct) =>
        CambiarAsync(id, handler.EmitirAsync, "Se emitió la liquidación.", ct);

    public Task<IActionResult> OnPostPagadaAsync(Guid id, CancellationToken ct) =>
        CambiarAsync(id, handler.MarcarPagadaAsync, "Se registró el pago de la liquidación.", ct);

    // Eliminada, la liquidación ya no tiene detalle: vuelve al listado con el aviso.
    public async Task<IActionResult> OnPostEliminarAsync(Guid id, CancellationToken ct)
    {
        try
        {
            if (!await handler.EliminarAsync(id, ct))
                return NotFound();
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
            return LocalRedirect($"/backoffice/liquidaciones/{id}");
        }
        TempData["Mensaje"] = "Se eliminó el borrador. Sus envíos pueden entrar en otra liquidación.";
        return LocalRedirect("/backoffice/liquidaciones");
    }

    private async Task<IActionResult> CambiarAsync(Guid id, Func<Guid, CancellationToken, Task<bool>> cambio,
        string mensaje, CancellationToken ct)
    {
        try
        {
            if (!await cambio(id, ct))
                return NotFound();
            TempData["Mensaje"] = mensaje;
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
        }
        return LocalRedirect($"/backoffice/liquidaciones/{id}");
    }
}
