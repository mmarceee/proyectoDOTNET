using System.ComponentModel.DataAnnotations;
using Logistica.Modules.Administracion.Application.Features.IniciarSesion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.IniciarSesion;

// Inicio de sesión del personal y de los comercios. Página del Backoffice en Presentation (ADR-0001, addendum 4):
// verifica la contraseña a través de IServicioSesion, sin conocer Identity.
internal sealed class IngresarModel(IServicioSesion sesion)
    : IngresoModel(sesion, TipoAccesoSesion.Personal, "/backoffice") { }

internal abstract class IngresoModel(IServicioSesion sesion, TipoAccesoSesion acceso, string inicio) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Ingresá tu correo.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá tu contraseña.")]
    [DataType(DataType.Password)]
    public string Contrasena { get; set; } = "";

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

        var resultado = await sesion.IniciarAsync(Email, Contrasena, acceso);

        if (resultado == ResultadoInicioSesion.Iniciada)
        {
            // Siempre comenzar en el inicio, incluso si la cookie agrega un ReturnUrl al login.
            return LocalRedirect(inicio);
        }

        Error = resultado == ResultadoInicioSesion.Bloqueada
            ? "La cuenta está bloqueada por varios intentos fallidos. Probá de nuevo en unos minutos."
            : "El correo o la contraseña no son correctos.";

        return Page();
    }
}
