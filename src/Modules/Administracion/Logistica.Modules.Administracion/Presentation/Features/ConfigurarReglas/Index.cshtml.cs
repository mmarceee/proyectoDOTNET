using Logistica.Modules.Administracion.Application.Features.ConfigurarReglas;
using Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.ConfigurarReglas;

// CU-06, pasos 1 a 3: las versiones de las reglas operativas y "Nueva versión". Muestra las reglas que rigen ahora con
// la misma lectura con caché que usan Envíos y la PWA: sirve para ver el cambio en caliente (escenario 5) y la métrica de
// aciertos. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(
    ConsultarReglasHandler consultar, GestionarBorradorReglasHandler borrador, IAdministracionModuleApi administracion) : PageModel
{
    public IReadOnlyList<VersionReglasResumenDto> Versiones { get; private set; } = [];
    public ReglasOperativas? Vigentes { get; private set; }
    public bool HayBorrador { get; private set; }
    public string? Mensaje { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        Versiones = await consultar.ListarAsync(ct);
        Vigentes = await administracion.ReglasDeEntregaVigentesAsync(ct);
        HayBorrador = Versiones.Any(v => v.Situacion == SituacionVersion.Borrador);
    }

    // Si ya hay borrador, se abre ése.
    public async Task<IActionResult> OnPostNuevaAsync(CancellationToken ct)
    {
        try
        {
            await borrador.NuevaVersionAsync(ct);
            return LocalRedirect("/backoffice/reglas/borrador");
        }
        catch (DomainException e)
        {
            TempData["Mensaje"] = e.Message;
            return LocalRedirect("/backoffice/reglas");
        }
    }
}
