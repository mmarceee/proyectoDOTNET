using System.Security.Claims;
using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarUsuarios;

// CU-02: el detalle de un usuario, con cambiar el perfil (A3), desactivar (A4) y reenviar la invitación (A6).
// Un usuario de otro operador responde 404, como si no existiera (ADR-0002, sección 2.9).
[Authorize(Policy = Politicas.Administrador)]
internal sealed class DetalleModel(
    ConsultarUsuariosHandler consultar, ModificarUsuarioHandler modificar, IHostEnvironment entorno) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public Perfil NuevoPerfil { get; set; }

    public UsuarioVista Usuario { get; private set; } = null!;
    public string? Mensaje { get; private set; }
    public string? Enlace { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        Enlace = TempData["Enlace"] as string;
        return await MostrarAsync(ct);
    }

    public Task<IActionResult> OnPostPerfilAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => await modificar.CambiarPerfilAsync(Id, NuevoPerfil, ct)
            ? "Se cambió el perfil. Rige en su próxima validación de sesión (a lo sumo, un minuto)."
            : null, ct);
    }

    public Task<IActionResult> OnPostDesactivarAsync(CancellationToken ct)
    {
        // El administrador de la sesión, para la regla A5 (nadie se desactiva a sí mismo).
        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId);

        return EjecutarAsync(async () => await modificar.DesactivarAsync(Id, actorId, ct)
            ? "Se desactivó el usuario. Sus sesiones abiertas se cierran en menos de un minuto."
            : null, ct);
    }

    public Task<IActionResult> OnPostReenviarAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () =>
        {
            if (await modificar.ReenviarInvitacionAsync(Id, ct) is not Invitacion invitacion)
            {
                return null;
            }

            if (entorno.IsDevelopment())
            {
                TempData["Enlace"] = invitacion.Enlace;
            }

            return "Se reenvió la invitación.";
        }, ct);
    }

    // Ejecuta la acción: con éxito vuelve al detalle con el mensaje; null es "no es de este operador" (404); una regla
    // rota (A5, perfiles) se muestra en la página.
    private async Task<IActionResult> EjecutarAsync(Func<Task<string?>> accion, CancellationToken ct)
    {
        try
        {
            if (await accion() is not string mensaje)
            {
                return NotFound();
            }

            TempData["Mensaje"] = mensaje;
            return LocalRedirect($"/backoffice/usuarios/{Id}");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return await MostrarAsync(ct);
        }
    }

    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(Id, ct) is not UsuarioVista usuario)
        {
            return NotFound();
        }

        Usuario = usuario;
        NuevoPerfil = usuario.Perfil;
        return Page();
    }
}
