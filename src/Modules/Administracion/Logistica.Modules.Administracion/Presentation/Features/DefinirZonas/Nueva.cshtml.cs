using System.ComponentModel.DataAnnotations;
using Logistica.Modules.Administracion.Application.Features.DefinirZonas;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.DefinirZonas;

// CU-04, alta (pasos 3, 5 y 6). Las franjas se agregan después, en el detalle de la zona. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class NuevaModel(GestionarZonasHandler gestionar) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Ingresá el código.")]
    public string Codigo { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá el nombre.")]
    public string Nombre { get; set; } = "";

    // Separados por comas, espacios o saltos de línea.
    [BindProperty]
    [Required(ErrorMessage = "Ingresá al menos un código postal.")]
    public string CodigosPostales { get; set; } = "";

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await gestionar.CrearAsync(Codigo, Nombre, TextosZona.SepararCodigosPostales(CodigosPostales), ct);
                TempData["Mensaje"] = "Se creó la zona. Ahora agregá sus franjas horarias.";
                return LocalRedirect($"/backoffice/zonas/{id}");
            }
            catch (DomainException e)
            {
                // A1 y A2: se indica el error y no se crea nada.
                ModelState.AddModelError(string.Empty, e.Message);
            }
        }

        return Page();
    }
}
