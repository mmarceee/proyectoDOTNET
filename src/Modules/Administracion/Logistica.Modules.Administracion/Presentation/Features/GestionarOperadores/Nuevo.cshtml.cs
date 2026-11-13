using System.ComponentModel.DataAnnotations;
using Logistica.Modules.Administracion.Application.Features.GestionarOperadores;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarOperadores;

// Administrador general: alta de un operador con su primer administrador, que recibe la invitación como en CU-02.
[Authorize(Policy = Politicas.AdministradorGeneral)]
internal sealed class NuevoModel(GestionarOperadoresHandler operadores, IHostEnvironment entorno) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Ingresá el nombre del operador.")]
    public string Nombre { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá el slug.")]
    public string Slug { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá la zona horaria.")]
    public string ZonaHoraria { get; set; } = "America/Montevideo";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá el nombre del administrador.")]
    public string NombreAdministrador { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá el correo del administrador.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    public string EmailAdministrador { get; set; } = "";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var creado = await operadores.CrearAsync(
                new CrearOperadorCommand(Nombre, Slug, ZonaHoraria, NombreAdministrador, EmailAdministrador), ct);

            TempData["Mensaje"] = "Se creó el operador. Su administrador recibe un correo para definir la contraseña.";
            if (entorno.IsDevelopment())
            {
                TempData["Enlace"] = creado.Invitacion.Enlace;
            }

            return LocalRedirect($"/backoffice/plataforma/operadores/{creado.OperadorId}");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return Page();
        }
    }
}
