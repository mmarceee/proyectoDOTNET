using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.Modules.Envios.Application.Features.GestionarIncidencias;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Envios.Presentation.Features.GestionarIncidencias;

// CU-22, pasos 1 a 3: el despachador o el administrador registra una incidencia desde el detalle del envío.
[Authorize(Roles = "Administrador,Despachador")]
internal sealed class NuevaModel(ConsultarDetalleEnvioHandler detalle, GestionarIncidenciasHandler incidencias) : PageModel
{
    [BindProperty]
    public TipoIncidencia? Tipo { get; set; }

    [BindProperty]
    public string? Descripcion { get; set; }

    public string Numero { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string numero, CancellationToken ct)
    {
        return await CargarAsync(numero, ct) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(string numero, CancellationToken ct)
    {
        // Se avisa antes de enviar; las reglas las vuelve a controlar Incidencia.
        if (Tipo is null)
        {
            ModelState.AddModelError(nameof(Tipo), "Elegí el tipo de incidencia.");
        }

        if (string.IsNullOrWhiteSpace(Descripcion))
        {
            ModelState.AddModelError(nameof(Descripcion), "Describí qué pasó.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                var registrada = await incidencias.RegistrarAsync(new RegistrarIncidenciaCommand(numero, Tipo!.Value, Descripcion!), ct);

                // 404 genérico: no distingue un envío inexistente de uno de otro inquilino (ADR-0002, sección 2.9).
                return registrada ? LocalRedirect($"/backoffice/envios/{Uri.EscapeDataString(numero)}") : NotFound();
            }
            catch (DomainException e)
            {
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
        return true;
    }
}
