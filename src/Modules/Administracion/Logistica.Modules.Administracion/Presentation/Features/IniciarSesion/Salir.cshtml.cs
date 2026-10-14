using Logistica.Modules.Administracion.Application.Features.IniciarSesion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.IniciarSesion;

// Cierra la sesión. Sólo por POST, con el token antifalsificación de Razor Pages: un enlace de otro sitio no puede
// cerrarle la sesión a nadie. Abrirla por GET sólo lleva a la página de inicio de sesión.
internal sealed class SalirModel(IServicioSesion sesion) : PageModel
{
    public IActionResult OnGet()
    {
        return LocalRedirect("/cuenta/ingresar");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await sesion.CerrarAsync();
        return LocalRedirect("/cuenta/ingresar");
    }
}
