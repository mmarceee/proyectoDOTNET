using Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.ConfigurarTarifario;

// CU-05, paso 2: una versión publicada, sólo para ver (no se modifica, letra 6.6). Una de otro operador responde 404
// (ADR-0002, sección 2.9). Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class DetalleModel(ConsultarTarifarioHandler consultar) : PageModel
{
    public VersionDetalleDto Version { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int numero, CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(numero, ct) is not VersionDetalleDto version)
        {
            return NotFound();
        }

        if (version.Situacion == SituacionVersion.Borrador)
        {
            return LocalRedirect("/backoffice/tarifario/borrador");
        }

        Version = version;
        return Page();
    }
}
