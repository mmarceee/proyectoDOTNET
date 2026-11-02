using Logistica.Modules.Administracion.Application.Features.GestionarFlota;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarFlota;

// CU-08: el detalle de un vehículo, con modificar (A2) y desactivar (A3). Uno de otro operador responde 404, como si no
// existiera (ADR-0002, sección 2.9). Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class VehiculoModel(ConsultarFlotaHandler consultar, GestionarFlotaHandler gestionar) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public string Tipo { get; set; } = "";

    [BindProperty]
    public decimal CapacidadPesoKg { get; set; }

    [BindProperty]
    public decimal CapacidadVolumenM3 { get; set; }

    [BindProperty]
    public decimal LargoCargaCm { get; set; }

    [BindProperty]
    public decimal AnchoCargaCm { get; set; }

    [BindProperty]
    public decimal AltoCargaCm { get; set; }

    public VehiculoDto Vehiculo { get; private set; } = null!;
    public string? Mensaje { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        return MostrarAsync(ct);
    }

    public Task<IActionResult> OnPostModificarAsync(CancellationToken ct)
    {
        var datos = new DatosVehiculo(Tipo, CapacidadPesoKg, CapacidadVolumenM3, LargoCargaCm, AnchoCargaCm, AltoCargaCm);
        return EjecutarAsync(async () => await gestionar.ModificarVehiculoAsync(Id, datos, ct)
            ? "Se guardaron los cambios. Las rutas ya validadas conservan su evidencia de carga."
            : null, ct);
    }

    public Task<IActionResult> OnPostDesactivarAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.DesactivarVehiculoAsync(Id, ct)
            ? "Se desactivó el vehículo: no se ofrece para rutas nuevas."
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
            return LocalRedirect($"/backoffice/flota/vehiculos/{Id}");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return await MostrarAsync(ct);
        }
    }

    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await consultar.VehiculoAsync(Id, ct) is not VehiculoDto vehiculo)
        {
            return NotFound();
        }

        // Los campos muestran lo guardado; tras un error, los que se enviaron ganan (ModelState).
        Vehiculo = vehiculo;
        Tipo = vehiculo.Tipo;
        CapacidadPesoKg = vehiculo.CapacidadPesoKg;
        CapacidadVolumenM3 = vehiculo.CapacidadVolumenM3;
        LargoCargaCm = vehiculo.LargoCargaCm;
        AnchoCargaCm = vehiculo.AnchoCargaCm;
        AltoCargaCm = vehiculo.AltoCargaCm;
        return Page();
    }
}
