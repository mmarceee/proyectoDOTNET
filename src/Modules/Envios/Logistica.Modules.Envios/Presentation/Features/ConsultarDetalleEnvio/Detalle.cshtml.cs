using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;

namespace Logistica.Modules.Envios.Presentation.Features.ConsultarDetalleEnvio;

internal sealed class DetalleModel(ConsultarDetalleEnvioHandler handler) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int TamanoPagina { get; set; } = 20;

    [BindProperty(SupportsGet = true)]
    public EstadoEnvio? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaDesde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaHasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Texto { get; set; }

    public EnvioDetalleDto Detalle { get; private set; } = null!;

    // El destino siempre es el listado; sólo se conservan sus parámetros de navegación.
    public string UrlVolver => QueryHelpers.AddQueryString("/backoffice/envios",
        new Dictionary<string, string?>
        {
            [nameof(Pagina)] = Pagina == 1 ? null : Pagina.ToString(CultureInfo.InvariantCulture),
            [nameof(TamanoPagina)] = TamanoPagina == 20 ? null : TamanoPagina.ToString(CultureInfo.InvariantCulture),
            [nameof(Estado)] = Estado?.ToString(),
            [nameof(FechaDesde)] = FechaDesde?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [nameof(FechaHasta)] = FechaHasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [nameof(Texto)] = string.IsNullOrWhiteSpace(Texto) ? null : Texto,
        });

    public async Task<IActionResult> OnGetAsync(string numero, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(numero))
        {
            return NotFound();
        }

        var detalle = await handler.HandleAsync(new(numero), ct);
        if (detalle is null)
        {
            return NotFound();
        }

        Detalle = detalle;
        return Page();
    }

    public string MostrarFecha(DateTimeOffset fecha) =>
        TimeZoneInfo.ConvertTime(fecha, TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo"))
            .ToString("dd/MM/yyyy HH:mm");
}
