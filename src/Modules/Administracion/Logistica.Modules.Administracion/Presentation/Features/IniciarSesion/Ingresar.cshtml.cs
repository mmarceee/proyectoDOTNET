using System.ComponentModel.DataAnnotations;
using Logistica.Modules.Administracion.Application.Features.IniciarSesion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.IniciarSesion;

// Inicio de sesión del personal y de los comercios. Página del Backoffice en Presentation (ADR-0001, addendum 4):
// verifica la contraseña a través de IServicioSesion, sin conocer Identity.
internal sealed class IngresarModel(IServicioSesion sesion) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Ingresá tu correo.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá tu contraseña.")]
    [DataType(DataType.Password)]
    public string Contrasena { get; set; } = "";

    // A dónde volver después de entrar. La cookie lo agrega cuando manda a alguien a iniciar sesión.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var resultado = await sesion.IniciarAsync(Email, Contrasena);

        if (resultado == ResultadoInicioSesion.Iniciada)
        {
            // Sólo direcciones de este mismo sitio: un ReturnUrl a otro dominio no se sigue (redirección abierta).
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/backoffice");
        }

        Error = resultado == ResultadoInicioSesion.Bloqueada
            ? "La cuenta está bloqueada por varios intentos fallidos. Probá de nuevo en unos minutos."
            : "El correo o la contraseña no son correctos.";

        return Page();
    }
}
