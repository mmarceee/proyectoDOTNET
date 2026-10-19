using Logistica.Modules.Administracion.Application.Features.ConfigurarReglas;
using Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.ConfigurarReglas;

// CU-06, paso 2: una versión publicada, sólo para ver. Una de otro operador responde 404 (ADR-0002, sección 2.9).
[Authorize(Policy = Politicas.Administrador)]
internal sealed class DetalleModel(ConsultarReglasHandler consultar) : PageModel
{
    public VersionReglasDetalleDto Version { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int numero, CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(numero, ct) is not VersionReglasDetalleDto version)
        {
            return NotFound();
        }

        if (version.Situacion == SituacionVersion.Borrador)
        {
            return LocalRedirect("/backoffice/reglas/borrador");
        }

        Version = version;
        return Page();
    }
}
