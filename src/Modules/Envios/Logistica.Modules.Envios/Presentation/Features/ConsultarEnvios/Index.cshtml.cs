using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Logistica.Modules.Envios.Domain.Envios;
using System.Globalization;

namespace Logistica.Modules.Envios.Presentation.Features.ConsultarEnvios;

internal sealed class IndexModel(
    ConsultarEnviosHandler handler) : PageModel
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

    public ConsultarEnviosResult Resultado { get; private set; } =
        new([], 1, 20, 0);

    public Dictionary<string, string?> ParametrosListado =>
        new()
        {
            [nameof(Pagina)] = Resultado.Pagina == 1 ? null : Resultado.Pagina.ToString(CultureInfo.InvariantCulture),
            [nameof(TamanoPagina)] = Resultado.TamanoPagina == 20 ? null : Resultado.TamanoPagina.ToString(CultureInfo.InvariantCulture),
            [nameof(Estado)] = Estado?.ToString(),
            [nameof(FechaDesde)] = FechaDesde?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [nameof(FechaHasta)] = FechaHasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [nameof(Texto)] = string.IsNullOrWhiteSpace(Texto) ? null : Texto,
        };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Pagina < 1 || TamanoPagina < 1 || TamanoPagina > 100)
        {
            ModelState.AddModelError(
                "",
                "Los parámetros de paginación son inválidos.");
        }

        if (Estado.HasValue && !Enum.IsDefined(Estado.Value))
        {
            ModelState.AddModelError(
                nameof(Estado),
                "El estado indicado no es válido.");
        }

        if (FechaDesde.HasValue &&
            FechaHasta.HasValue &&
            FechaDesde.Value > FechaHasta.Value)
        {
            ModelState.AddModelError(
                nameof(FechaHasta),
                "La fecha desde no puede ser posterior a la fecha hasta.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var query = new ConsultarEnviosQuery(
            Pagina,
            TamanoPagina,
            Estado,
            FechaDesde,
            FechaHasta,
            Texto);

        Resultado = await handler.HandleAsync(query, ct);

        return Page();
    }
}
