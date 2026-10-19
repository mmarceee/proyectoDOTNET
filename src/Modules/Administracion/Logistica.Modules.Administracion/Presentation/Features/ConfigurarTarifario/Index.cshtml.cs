using Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.ConfigurarTarifario;

// CU-05, pasos 1 a 3: las versiones del cuadro tarifario y "Nueva versión". Incluye un simulador de precio que usa la
// misma lectura con caché que el alta de envíos (CU-10): sirve para ver el cambio en caliente (escenario 5) y la métrica
// de aciertos en Aspire Dashboard. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(
    ConsultarTarifarioHandler consultar, GestionarBorradorHandler borrador, IAdministracionModuleApi administracion) : PageModel
{
    public IReadOnlyList<VersionResumenDto> Versiones { get; private set; } = [];
    public IReadOnlyList<ZonaOpcionDto> Zonas { get; private set; } = [];
    public bool HayBorrador { get; private set; }
    public string? Mensaje { get; private set; }

    [BindProperty]
    public Guid ZonaId { get; set; }

    [BindProperty]
    public Modalidad Modalidad { get; set; }

    [BindProperty]
    public string PesoKg { get; set; } = "2";

    [BindProperty]
    public string VolumenM3 { get; set; } = "0.01";

    public string? Precio { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        await CargarAsync(ct);
    }

    // A4: si ya hay borrador, se abre ése.
    public async Task<IActionResult> OnPostNuevaAsync(CancellationToken ct)
    {
        await borrador.NuevaVersionAsync(ct);
        return LocalRedirect("/backoffice/tarifario/borrador");
    }

    public async Task<IActionResult> OnPostCalcularAsync(CancellationToken ct)
    {
        try
        {
            var tarifa = await administracion.CalcularTarifaAsync(ZonaId, Modalidad,
                TextosTarifario.Decimal(PesoKg, "el peso"), TextosTarifario.Decimal(VolumenM3, "el volumen"), ct);
            Precio = tarifa is null
                ? "Sin precio: ninguna regla de la versión vigente cubre ese bulto (el alta lo rechazaría)."
                : $"$ {TextosTarifario.Numero(tarifa.Monto)} con la versión {tarifa.VersionTarifarioNumero}.";
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
        }

        await CargarAsync(ct);
        return Page();
    }

    private async Task CargarAsync(CancellationToken ct)
    {
        Versiones = await consultar.ListarAsync(ct);
        Zonas = await consultar.ZonasActivasAsync(ct);
        HayBorrador = Versiones.Any(v => v.Situacion == SituacionVersion.Borrador);
    }
}
