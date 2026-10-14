using Logistica.Modules.Administracion.Application.Features.IniciarSesion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.IniciarSesion;

// Cierra la sesión sólo por POST con antiforgery. GET muestra la confirmación, sin cerrar la cookie.
internal sealed class SalirModel(IServicioSesion sesion) : PageModel
{
    [BindProperty(SupportsGet = true)] public bool Portal { get; set; }
    [BindProperty(SupportsGet = true)] public bool Repartidor { get; set; }
    private string Login => Repartidor ? "/repartidor/ingresar" : Portal ? "/portal/ingresar" : "/cuenta/ingresar";
    public IActionResult OnGet()
    {
        return User.Identity?.IsAuthenticated == true ? Page() : LocalRedirect(Login);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await sesion.CerrarAsync();
        return LocalRedirect(Login);
    }
}
