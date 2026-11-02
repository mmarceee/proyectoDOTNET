using Logistica.Modules.Administracion.Application.Features.GestionarFlota;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarFlota;

// CU-08: el detalle de un repartidor, con modificar y desactivar (A3). Uno de otro operador responde 404, como si no
// existiera (ADR-0002, sección 2.9). Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class RepartidorModel(ConsultarFlotaHandler consultar, GestionarFlotaHandler gestionar) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public string Nombre { get; set; } = "";

    public RepartidorDto Repartidor { get; private set; } = null!;
    public string? Mensaje { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        return MostrarAsync(ct);
    }

    public Task<IActionResult> OnPostModificarAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.ModificarRepartidorAsync(Id, Nombre, ct)
            ? "Se guardaron los cambios."
            : null, ct);
    }

    public Task<IActionResult> OnPostDesactivarAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.DesactivarRepartidorAsync(Id, ct)
            ? "Se desactivó el repartidor: no se ofrece para rutas nuevas."
            : null, ct);
    }

    // Con éxito vuelve al detalle con el mensaje; null es "no es de este operador" (404); una regla rota se muestra.
    private async Task<IActionResult> EjecutarAsync(Func<Task<string?>> accion, CancellationToken ct)
    {
        try
        {
            if (await accion() is not string mensaje)
            {
                return NotFound();
            }

            TempData["Mensaje"] = mensaje;
            return LocalRedirect($"/backoffice/flota/repartidores/{Id}");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return await MostrarAsync(ct);
        }
    }

    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await consultar.RepartidorAsync(Id, ct) is not RepartidorDto repartidor)
        {
            return NotFound();
        }

        Repartidor = repartidor;
        Nombre = repartidor.Nombre;
        return Page();
    }
}
