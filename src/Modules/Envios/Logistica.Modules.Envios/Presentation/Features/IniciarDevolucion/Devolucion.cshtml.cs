using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.Modules.Envios.Application.Features.IniciarDevolucion;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Envios.Presentation.Features.IniciarDevolucion;

// CU-20 desde el Backoffice: el despachador decide devolver un envío al comercio (T12 o T14). Pide el motivo (paso 2).
[Authorize(Policy = Politicas.Despachador)]
internal sealed class DevolucionModel(
    ConsultarDetalleEnvioHandler detalle, IEnviosModuleApi envios, IniciarDevolucionHandler iniciar) : PageModel
{
    [BindProperty]
    public string? Motivo { get; set; }

    public string Numero { get; private set; } = "";
    public string Estado { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string numero, CancellationToken ct)
    {
        return await CargarAsync(numero, ct) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(string numero, CancellationToken ct)
    {
        // Sin motivo no se confirma. La regla la vuelve a controlar Envio; acá sólo se avisa antes.
        if (string.IsNullOrWhiteSpace(Motivo))
        {
            ModelState.AddModelError(nameof(Motivo), "Indicá el motivo de la devolución.");
        }

        if (ModelState.IsValid)
        {
            // El número lo resuelve el filtro "Tenant": un envío de otro operador no existe (404).
            if (await envios.IdPorNumeroAsync(numero, ct) is not Guid id)
            {
                return NotFound();
            }

            try
            {
                var responsable = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : (Guid?)null;
                var iniciada = await iniciar.HandleAsync(
                    new IniciarDevolucionCommand(id, Motivo!, OrigenEvento.Backoffice, responsable), ct);

                return iniciada ? LocalRedirect($"/backoffice/envios/{Uri.EscapeDataString(numero)}") : NotFound();
            }
            catch (DomainException e)
            {
                // A1: el estado ya no lo permite (por ejemplo, salió a reparto mientras el usuario confirmaba).
                ModelState.AddModelError(string.Empty, e.Message);
            }
        }

        return await CargarAsync(numero, ct) ? Page() : NotFound();
    }

    private async Task<bool> CargarAsync(string numero, CancellationToken ct)
    {
        var envio = await detalle.HandleAsync(new(numero), ct);
        if (envio is null)
        {
            return false;
        }

        Numero = envio.Numero;
        Estado = envio.Estado;
        return true;
    }
}
