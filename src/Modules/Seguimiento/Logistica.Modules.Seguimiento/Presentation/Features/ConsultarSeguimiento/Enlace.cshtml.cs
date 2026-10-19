using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Seguimiento.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Seguimiento.Presentation.Features.ConsultarSeguimiento;

// CU-60: el enlace público de seguimiento de un envío, para que el personal lo copie y lo mande (mientras el aviso por
// correo no lo incluya). Un envío de otro operador no se encuentra: 404 (ADR-0002, sección 2.9).
[Authorize(Roles = "Administrador,Despachador")]
internal sealed class EnlaceModel(IEnviosModuleApi envios, ISeguimientoModuleApi seguimiento) : PageModel
{
    public string Numero { get; private set; } = "";
    public string Direccion { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string numero, CancellationToken ct)
    {
        if (await envios.IdPorNumeroAsync(numero, ct) is not Guid envioId)
        {
            return NotFound();
        }

        Numero = numero.Trim().ToUpperInvariant();
        Direccion = $"{Request.Scheme}://{Request.Host}{seguimiento.EnlaceDe(envioId)}";
        return Page();
    }
}
