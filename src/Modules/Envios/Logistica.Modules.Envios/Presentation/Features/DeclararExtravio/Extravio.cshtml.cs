using System.Security.Claims;
using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.Modules.Envios.Application.Features.DeclararExtravio;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Envios.Presentation.Features.DeclararExtravio;

// CU-21: el administrador o el despachador declara extraviado un envío. Es irreversible, así que la página pide el
// motivo y una confirmación explícita (requerimiento especial 1).
[Authorize(Roles = "Administrador,Despachador")]
internal sealed class ExtravioModel(ConsultarDetalleEnvioHandler detalle, DeclararExtravioHandler declarar) : PageModel
{
    [BindProperty]
    public string? Motivo { get; set; }

    [BindProperty]
    public bool Confirmo { get; set; }

    public string Numero { get; private set; } = "";
    public string Estado { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string numero, CancellationToken ct)
    {
        return await CargarAsync(numero, ct) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(string numero, CancellationToken ct)
    {
        // A1: sin motivo no se confirma. La regla la vuelve a controlar Envio; acá sólo se avisa antes.
        if (string.IsNullOrWhiteSpace(Motivo))
        {
            ModelState.AddModelError(nameof(Motivo), "Indicá el motivo del extravío.");
        }

        if (!Confirmo)
        {
            ModelState.AddModelError(nameof(Confirmo), "Confirmá que entendés que no se puede deshacer.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                var responsable = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;
                var declarado = await declarar.HandleAsync(new DeclararExtravioCommand(numero, Motivo!, responsable), ct);

                // 404 genérico: no distingue un envío inexistente de uno de otro inquilino (ADR-0002, sección 2.9).
                return declarado ? LocalRedirect($"/backoffice/envios/{Uri.EscapeDataString(numero)}") : NotFound();
            }
            catch (DomainException e)
            {
                // A2: el estado ya no lo permite (por ejemplo, se entregó mientras el usuario confirmaba).
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
