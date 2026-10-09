using Logistica.Modules.Deposito.Application.Features.RecibirBulto;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Logistica.Modules.Envios.Contracts;

namespace Logistica.Modules.Deposito.Presentation.Features.RecibirBulto;

// Pantalla de recepción del operario (CU-30). Delgada como un endpoint: arma el command y muestra el resultado.
internal sealed class IndexModel(RecibirBultoHandler handler, IEnviosModuleApi envios) : PageModel
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

    public RecepcionRegistrada? Resultado { get; private set; }
    public string? NumeroEnvio { get; private set; }
    public string? UrlDetalle => NumeroEnvio is null ? null : $"/backoffice/envios/{Uri.EscapeDataString(NumeroEnvio)}";

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
        CodigoBulto = (CodigoBulto ?? "").Trim().ToUpperInvariant();
        var envio = await envios.ObtenerParaRecepcionAsync(CodigoBulto, ct);
        NumeroEnvio = envio?.Numero;
        return envio is not null;
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
