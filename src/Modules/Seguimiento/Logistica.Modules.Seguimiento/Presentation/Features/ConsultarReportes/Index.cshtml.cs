using Logistica.Modules.Seguimiento.Application.Features.ConsultarReportes;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Seguimiento.Presentation.Features.ConsultarReportes;

// CU-72: el administrador o el despachador elige el reporte y el período (pasos 1 y 2), lo ve en una tabla (paso 3) y
// lo descarga en CSV (paso 4). Todo por GET: consultar no modifica datos, y el enlace se puede compartir.
[Authorize(Roles = "Administrador,Despachador")]
internal sealed class IndexModel(ConsultarReportesHandler handler, TimeProvider reloj) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public TipoReporte Tipo { get; set; } = TipoReporte.Cumplimiento;

    [BindProperty(SupportsGet = true)]
    public DimensionCumplimiento Dimension { get; set; } = DimensionCumplimiento.Zona;

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    public TablaReporte? Tabla { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Tabla = await ConsultarAsync(ct);
    }

    public async Task<IActionResult> OnGetCsvAsync(CancellationToken ct)
    {
        Tabla = await ConsultarAsync(ct);
        if (Tabla is null) return Page();
        var nombre = $"reporte-{Tipo.ToString().ToLowerInvariant()}-{Desde:yyyy-MM-dd}-al-{Hasta:yyyy-MM-dd}.csv";
        return File(Tabla.Csv(Desde!.Value, Hasta!.Value), "text/csv; charset=utf-8", nombre);
    }

    // Por defecto, del primer día del mes a hoy. null si el período es inválido (A2): el motivo queda en ModelState.
    private async Task<TablaReporte?> ConsultarAsync(CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        Desde ??= new DateOnly(hoy.Year, hoy.Month, 1);
        Hasta ??= hoy;
        try
        {
            var reporte = await handler.HandleAsync(new ConsultarReporteQuery(Tipo, Desde.Value, Hasta.Value, Dimension), ct);
            return TablaReporte.Desde(reporte);
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return null;
        }
    }
}
