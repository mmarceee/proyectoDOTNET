using System.ComponentModel.DataAnnotations;
using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.ActivarCuenta;

// CU-02: el usuario invitado define su contraseña con el enlace del correo. Sin sesión: la credencial es el token,
// que vence a las 48 horas y sirve una sola vez.
internal sealed class ActivarModel(IServicioUsuarios usuarios) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Usuario { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá una contraseña.")]
    [DataType(DataType.Password)]
    public string Contrasena { get; set; } = "";

    [BindProperty]
    [DataType(DataType.Password)]
    [Compare(nameof(Contrasena), ErrorMessage = "Las contraseñas no coinciden.")]
    public string Confirmacion { get; set; } = "";

    public bool Activada { get; private set; }
    public bool EnlaceInvalido { get; private set; }

    // Después de activar, cada perfil entra por la página de su aplicación.
    public string Ingreso { get; private set; } = "/cuenta/ingresar";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var resultado = await usuarios.ActivarAsync(Usuario, Token, Contrasena);

        if (resultado.Activada)
        {
            Activada = true;
            Ingreso = resultado.Perfil switch
            {
                Perfil.UsuarioComercio => "/portal/ingresar",
                Perfil.Repartidor => "/repartidor/ingresar",
                _ => "/cuenta/ingresar",
            };
        }
        else if (resultado.EnlaceInvalido)
        {
            EnlaceInvalido = true;
        }
        else
        {
            foreach (var error in resultado.Errores)
            {
                ModelState.AddModelError(string.Empty, error);
            }
        }

        return Page();
    }
}
