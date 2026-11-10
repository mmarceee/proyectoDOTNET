using System.Globalization;
using Logistica.Modules.Envios.Application.Features.GestionarIncidencias;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Logistica.Modules.Envios.Presentation.Features.GestionarIncidencias;

// CU-22: el listado de incidencias del operador (A1) y, desde cada fila, pasarla a en proceso (paso 4) o resolverla
// (paso 5).
[Authorize(Roles = "Administrador,Despachador")]
internal sealed class IndexModel(ConsultarIncidenciasHandler consultar, GestionarIncidenciasHandler gestionar) : PageModel
{
    private const string Ruta = "/backoffice/incidencias";

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public EstadoIncidencia? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public TipoIncidencia? Tipo { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaDesde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaHasta { get; set; }

    public ConsultarIncidenciasResult Resultado { get; private set; } = new([], 1, 20, 0);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await CargarAsync(ct);
        return Page();
    }

    public Task<IActionResult> OnPostEnProcesoAsync(Guid id, CancellationToken ct) =>
        AplicarAsync(() => gestionar.PasarAEnProcesoAsync(id, ct), ct);

    public Task<IActionResult> OnPostResolverAsync(Guid id, string? resolucion, CancellationToken ct) =>
        AplicarAsync(() => gestionar.ResolverAsync(id, resolucion ?? "", ct), ct);

    // Después de un cambio vuelve al listado con los mismos filtros, así recargar la página no lo repite.
    private async Task<IActionResult> AplicarAsync(Func<Task<bool>> accion, CancellationToken ct)
    {
        try
        {
            // 404 genérico: no distingue una incidencia inexistente de una de otro inquilino (ADR-0002, sección 2.9).
            return await accion() ? LocalRedirect(UrlPagina(Pagina)) : NotFound();
        }
        catch (DomainException e)
        {
            // A2 (resolver sin resolución) o un estado que ya no lo permite.
            ModelState.AddModelError(string.Empty, e.Message);
            await CargarAsync(ct);
            return Page();
        }
    }

    private async Task CargarAsync(CancellationToken ct)
    {
        if (Pagina < 1)
        {
            Pagina = 1;
        }

        if (FechaDesde is { } desde && FechaHasta is { } hasta && desde > hasta)
        {
            ModelState.AddModelError(nameof(FechaHasta), "La fecha desde no puede ser posterior a la fecha hasta.");
            return;
        }

        if ((Estado is { } estado && !Enum.IsDefined(estado)) || (Tipo is { } tipo && !Enum.IsDefined(tipo)))
        {
            ModelState.AddModelError(string.Empty, "El filtro indicado no es válido.");
            return;
        }

        Resultado = await consultar.HandleAsync(new ConsultarIncidenciasQuery(Pagina, 20, Estado, Tipo, FechaDesde, FechaHasta), ct);
    }

    public string UrlPagina(int pagina) => QueryHelpers.AddQueryString(Ruta, new Dictionary<string, string?>
    {
        [nameof(Pagina)] = pagina == 1 ? null : pagina.ToString(CultureInfo.InvariantCulture),
        [nameof(Estado)] = Estado?.ToString(),
        [nameof(Tipo)] = Tipo?.ToString(),
        [nameof(FechaDesde)] = FechaDesde?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        [nameof(FechaHasta)] = FechaHasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    });

    public string MostrarFecha(DateTimeOffset fecha) =>
        TimeZoneInfo.ConvertTime(fecha, TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo"))
            .ToString("dd/MM/yyyy HH:mm");
}
