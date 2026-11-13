using Logistica.Modules.Administracion.Application.Features.GestionarOperadores;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarOperadores;

// Administrador general: el detalle de un operador, con suspender o activar, y sus administradores (agregar y
// desactivar). Los demás usuarios del operador los gestiona su propio administrador (CU-02).
[Authorize(Policy = Politicas.AdministradorGeneral)]
internal sealed class DetalleModel(GestionarOperadoresHandler operadores, IHostEnvironment entorno) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    // Para agregar un administrador. Sin atributos de validación: los demás formularios de la página no los mandan;
    // el caso de uso valida el correo y Usuario el nombre.
    [BindProperty]
    public string NombreAdministrador { get; set; } = "";

    [BindProperty]
    public string EmailAdministrador { get; set; } = "";

    [BindProperty]
    public Guid UsuarioId { get; set; }

    public DetalleOperador Detalle { get; private set; } = null!;
    public string? Mensaje { get; private set; }
    public string? Enlace { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        Enlace = TempData["Enlace"] as string;
        return await MostrarAsync(ct);
    }

    public Task<IActionResult> OnPostSuspenderAsync(CancellationToken ct) =>
        EjecutarAsync(async () => await operadores.SuspenderAsync(Id, ct)
            ? "Se suspendió el operador. Su personal ya no puede iniciar sesión y sus comercios no lo pueden elegir."
            : null, ct);

    public Task<IActionResult> OnPostActivarAsync(CancellationToken ct) =>
        EjecutarAsync(async () => await operadores.ActivarAsync(Id, ct) ? "Se activó el operador." : null, ct);

    public Task<IActionResult> OnPostAdministradorAsync(CancellationToken ct) =>
        EjecutarAsync(async () =>
        {
            if (await operadores.AgregarAdministradorAsync(Id, NombreAdministrador, EmailAdministrador, ct) is not { } invitacion)
            {
                return null;
            }

            if (entorno.IsDevelopment())
            {
                TempData["Enlace"] = invitacion.Enlace;
            }

            return "Se creó el administrador. Le llega un correo para definir su contraseña.";
        }, ct);

    public Task<IActionResult> OnPostDesactivarAsync(CancellationToken ct) =>
        EjecutarAsync(async () => await operadores.DesactivarAdministradorAsync(Id, UsuarioId, ct)
            ? "Se desactivó el administrador. Sus sesiones abiertas se cierran en menos de un minuto."
            : null, ct);

    // Con éxito vuelve al detalle con el mensaje; null es "no existe" (404); una regla rota se muestra en la página.
    private async Task<IActionResult> EjecutarAsync(Func<Task<string?>> accion, CancellationToken ct)
    {
        try
        {
            if (await accion() is not string mensaje)
            {
                return NotFound();
            }

            TempData["Mensaje"] = mensaje;
            return LocalRedirect($"/backoffice/plataforma/operadores/{Id}");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return await MostrarAsync(ct);
        }
    }

    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await operadores.ObtenerAsync(Id, ct) is not { } detalle)
        {
            return NotFound();
        }

        Detalle = detalle;
        return Page();
    }
}
