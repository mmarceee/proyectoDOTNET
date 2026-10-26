using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.RecibirBulto;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Logistica.Modules.Deposito.Presentation.Features.RecibirBulto;

// Pantalla de recepción del operario (CU-30). Delgada como un endpoint: arma el command y muestra el resultado.
// Misma política que la API de recepción: el middleware deja entrar a todo el personal al Backoffice, pero recibir
// bultos es sólo del operario de depósito.
[Authorize(Policy = Politicas.OperarioDeposito)]
internal sealed class IndexModel(RecibirBultoHandler handler, ConsultarBultoParaRecepcionHandler consulta,
    IDataProtectionProvider proteccion) : PageModel
{
    private readonly IDataProtector protector = proteccion.CreateProtector("Deposito.Recepcion.ConfirmacionMedidas.v1");

    [BindProperty] public bool ConfirmarDiferencias { get; set; }
    [BindProperty] public string? ConfirmacionMedidas { get; set; }
    public IReadOnlyList<DiferenciaMedida> Diferencias { get; private set; } = [];

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
            if (!ModelState.IsValid)
            {
                await CargarEnvioAsync(ct);
                return Page();
            }

            var medidas = new Medidas(PesoKg, LargoCm, AnchoCm, AltoCm);
            var bulto = await consulta.HandleAsync(new(CodigoBulto), ct)
                ?? throw new DomainException("Bulto desconocido.");
            NumeroEnvio = bulto.NumeroEnvio;
            var diferencias = new List<DiferenciaMedida>();
            Comparar("Peso", "kg", bulto.Declaradas.PesoKg, PesoKg, diferencias);
            Comparar("Largo", "cm", bulto.Declaradas.LargoCm, LargoCm, diferencias);
            Comparar("Ancho", "cm", bulto.Declaradas.AnchoCm, AnchoCm, diferencias);
            Comparar("Alto", "cm", bulto.Declaradas.AltoCm, AltoCm, diferencias);
            var datos = JsonSerializer.Serialize(new { bulto.CodigoBulto, bulto.Declaradas, Medidas = medidas });
            if (diferencias.Count > 0 && !ConfirmacionValida(datos))
            {
                Diferencias = diferencias;
                ConfirmarDiferencias = false;
                ConfirmacionMedidas = protector.Protect(datos);
                ModelState.Remove(nameof(ConfirmarDiferencias));
                ModelState.Remove(nameof(ConfirmacionMedidas));
                return Page();
            }

            var command = new RecibirBultoCommand(CodigoBulto, medidas);
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
        ConfirmarDiferencias = false;
        ConfirmacionMedidas = null;

        return Page();
    }

    private bool ConfirmacionValida(string datos)
    {
        if (!ConfirmarDiferencias || string.IsNullOrEmpty(ConfirmacionMedidas)) return false;
        try { return protector.Unprotect(ConfirmacionMedidas) == datos; }
        catch (CryptographicException) { return false; }
    }

    private static void Comparar(string nombre, string unidad, decimal? declarado, decimal? medido,
        List<DiferenciaMedida> diferencias)
    {
        if (medido is not decimal valor) return;
        if (valor <= 0) throw new DomainException($"El valor de {nombre.ToLowerInvariant()} debe ser mayor que cero.");
        if (declarado is decimal original && original != valor)
            diferencias.Add(new(nombre, unidad, original, valor));
    }
}

internal sealed record DiferenciaMedida(string Nombre, string Unidad, decimal Declarado, decimal Medido);
