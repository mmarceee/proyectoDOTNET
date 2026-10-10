using Logistica.Modules.Deposito.Application.Features.RecibirBulto;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;

namespace Logistica.Modules.Deposito.Presentation.Features.RecibirBulto;

// Pantalla de recepción del operario (CU-30). Delgada como un endpoint: arma el command y muestra el resultado.
internal sealed class IndexModel(RecibirBultoHandler handler, ConsultarBultoParaRecepcionHandler consulta) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? CodigoBulto { get; set; }

    [BindProperty]
    public decimal? PesoKg { get; set; }

    [BindProperty]
    public decimal? LargoCm { get; set; }

    [BindProperty]
    public decimal? AnchoCm { get; set; }

    [BindProperty]
    public decimal? AltoCm { get; set; }

    // Contexto de navegación del listado; nunca se usa para autorizar ni registrar la recepción.
    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int TamanoPagina { get; set; } = 20;

    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaDesde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaHasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Texto { get; set; }

    public RecepcionRegistrada? Resultado { get; private set; }
    public string? NumeroEnvio { get; private set; }
    public string? UrlDetalle => NumeroEnvio is null ? null : QueryHelpers.AddQueryString(
        $"/backoffice/envios/{Uri.EscapeDataString(NumeroEnvio)}", new Dictionary<string, string?>
        {
            [nameof(Pagina)] = Pagina == 1 ? null : Pagina.ToString(CultureInfo.InvariantCulture),
            [nameof(TamanoPagina)] = TamanoPagina == 20 ? null : TamanoPagina.ToString(CultureInfo.InvariantCulture),
            [nameof(Estado)] = Estado,
            [nameof(FechaDesde)] = FechaDesde?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [nameof(FechaHasta)] = FechaHasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [nameof(Texto)] = string.IsNullOrWhiteSpace(Texto) ? null : Texto,
        });

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(CodigoBulto))
        {
            return Page();
        }

        var envio = await CargarEnvioAsync(ct);
        return envio ? Page() : NotFound();
    }

    private async Task<bool> CargarEnvioAsync(CancellationToken ct)
    {
        var bulto = await consulta.HandleAsync(new(CodigoBulto ?? ""), ct);
        NumeroEnvio = bulto?.NumeroEnvio;
        if (bulto is not null) CodigoBulto = bulto.CodigoBulto;
        return bulto is not null;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(CodigoBulto))
        {
            ModelState.AddModelError(nameof(CodigoBulto), "Escaneá o escribí el código del bulto.");
            return Page();
        }

        try
        {
            var command = new RecibirBultoCommand(CodigoBulto, new Medidas(PesoKg, LargoCm, AnchoCm, AltoCm));
            Resultado = await handler.HandleAsync(command, ct);
            NumeroEnvio = Resultado.NumeroEnvio;
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await CargarEnvioAsync(ct);
            return Page();
        }

        // Campos vacíos para el próximo escaneo.
        ModelState.Clear();
        CodigoBulto = "";
        PesoKg = LargoCm = AnchoCm = AltoCm = null;

        return Page();
    }
}
