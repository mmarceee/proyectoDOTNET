using System.ComponentModel.DataAnnotations;
using Logistica.Modules.Administracion.Application.Features.GestionarFlota;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarFlota;

// CU-08: el listado de repartidores y vehículos del operador, con el alta de cada uno. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(ConsultarFlotaHandler consultar, GestionarFlotaHandler gestionar) : PageModel
{
    // Alta de repartidor.
    [BindProperty]
    [Required(ErrorMessage = "Ingresá el nombre.")]
    public string Nombre { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá el documento.")]
    public string Documento { get; set; } = "";

    // Alta de vehículo. A2 (capacidades positivas) la valida Vehiculo.
    [BindProperty]
    [Required(ErrorMessage = "Ingresá la matrícula.")]
    public string Matricula { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá el tipo.")]
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

    public IReadOnlyList<RepartidorDto> Repartidores { get; private set; } = [];
    public IReadOnlyList<VehiculoDto> Vehiculos { get; private set; } = [];
    public string? Mensaje { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        await CargarAsync(ct);
    }

    public Task<IActionResult> OnPostRepartidorAsync(CancellationToken ct)
    {
        // Cada formulario valida sólo sus campos.
        foreach (var campo in new[] { nameof(Matricula), nameof(Tipo) }) ModelState.Remove(campo);
        return CrearAsync(async () =>
        {
            await gestionar.CrearRepartidorAsync(Nombre, Documento, ct);
            return "Se dio de alta el repartidor.";
        }, ct);
    }

    public Task<IActionResult> OnPostVehiculoAsync(CancellationToken ct)
    {
        foreach (var campo in new[] { nameof(Nombre), nameof(Documento) }) ModelState.Remove(campo);
        return CrearAsync(async () =>
        {
            await gestionar.CrearVehiculoAsync(Matricula, new DatosVehiculo(Tipo, CapacidadPesoKg, CapacidadVolumenM3,
                LargoCargaCm, AnchoCargaCm, AltoCargaCm), ct);
            return "Se dio de alta el vehículo.";
        }, ct);
    }

    // Con éxito vuelve al listado con el mensaje; una regla rota (A1, A2) se muestra sin perder lo escrito.
    private async Task<IActionResult> CrearAsync(Func<Task<string>> alta, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                TempData["Mensaje"] = await alta();
                return LocalRedirect("/backoffice/flota");
            }
            catch (DomainException e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
            }
        }

        await CargarAsync(ct);
        return Page();
    }

    private async Task CargarAsync(CancellationToken ct)
    {
        Repartidores = await consultar.RepartidoresAsync(ct);
        Vehiculos = await consultar.VehiculosAsync(ct);
    }
}
