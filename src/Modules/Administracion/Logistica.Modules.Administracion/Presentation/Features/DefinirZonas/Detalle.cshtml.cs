using Logistica.Modules.Administracion.Application.Features.DefinirZonas;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.DefinirZonas;

// CU-04: el detalle de una zona, con modificar (A4), desactivar (A5) y sus franjas (paso 4 y A4). Una zona de otro
// operador responde 404, como si no existiera (ADR-0002, sección 2.9). Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class DetalleModel(ConsultarZonasHandler consultar, GestionarZonasHandler gestionar) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    // A4: nombre y cobertura. El código no cambia.
    [BindProperty]
    public string Nombre { get; set; } = "";

    [BindProperty]
    public string CodigosPostales { get; set; } = "";

    // Paso 4: una franja nueva. Si indica a cuál reemplaza (A4), la anterior rige hasta el día antes de VigenteDesde.
    [BindProperty]
    public TimeOnly Desde { get; set; } = new(9, 0);

    [BindProperty]
    public TimeOnly Hasta { get; set; } = new(13, 0);

    [BindProperty]
    public List<DayOfWeek> Dias { get; set; } = [];

    // Por defecto, hoy en la zona horaria del operador (se completa en OnGetAsync): el servidor puede estar en UTC.
    [BindProperty]
    public DateOnly VigenteDesde { get; set; }

    [BindProperty]
    public Guid? ReemplazaA { get; set; }

    public ZonaDetalleDto Zona { get; private set; } = null!;
    public string? Mensaje { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        Dias = [.. TextosZona.DiasDeLaSemana.Take(5)];
        VigenteDesde = await gestionar.HoyAsync(ct);
        return await MostrarAsync(ct);
    }

    public Task<IActionResult> OnPostModificarAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () =>
            await gestionar.ModificarAsync(Id, Nombre, TextosZona.SepararCodigosPostales(CodigosPostales), ct)
                ? "Se guardaron los cambios. Los envíos ya admitidos conservan su zona."
                : null, ct);
    }

    public Task<IActionResult> OnPostDesactivarAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.DesactivarAsync(Id, ct)
            ? "Se desactivó la zona: no se acepta en envíos nuevos y los existentes no cambian."
            : null, ct);
    }

    public Task<IActionResult> OnPostFranjaAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => ReemplazaA is Guid anterior
            ? await gestionar.ReemplazarFranjaAsync(Id, anterior, Desde, Hasta, [.. Dias], VigenteDesde, ct)
                ? $"Se programó el cambio: la franja nueva rige desde el {VigenteDesde:dd/MM/yyyy}."
                : null
            : await gestionar.AgregarFranjaAsync(Id, Desde, Hasta, [.. Dias], VigenteDesde, ct)
                ? "Se agregó la franja."
                : null, ct);
    }

    // Ejecuta la acción: con éxito vuelve al detalle con el mensaje; null es "no es de este operador" (404); una regla
    // rota (A2, A3, valores inválidos) se muestra en la página, sin perder lo que escribió el administrador.
    private async Task<IActionResult> EjecutarAsync(Func<Task<string?>> accion, CancellationToken ct)
    {
        try
        {
            if (await accion() is not string mensaje)
            {
                return NotFound();
            }

            TempData["Mensaje"] = mensaje;
            return LocalRedirect($"/backoffice/zonas/{Id}");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return await MostrarAsync(ct);
        }
    }

    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(Id, ct) is not ZonaDetalleDto zona)
        {
            return NotFound();
        }

        // Los campos muestran lo guardado; tras un error, los que se enviaron ganan (ModelState).
        Zona = zona;
        Nombre = zona.Nombre;
        CodigosPostales = string.Join(", ", zona.CodigosPostales);
        return Page();
    }
}
