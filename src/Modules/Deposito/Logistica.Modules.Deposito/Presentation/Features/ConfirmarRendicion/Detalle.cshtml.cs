using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.ConfirmarRendicion;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Deposito.Presentation.Features.ConfirmarRendicion;

// CU-55, pasos 2 a 6: lo declarado, lo escaneado, la comparación y la confirmación. Delgada como un endpoint.
// Después de confirmar, o si la confirmación falla, se vuelve a la página con un GET nuevo: la transacción se deshizo,
// pero las entidades en seguimiento de este request pueden haber quedado cambiadas, y no hay que mostrarlas.
[Authorize(Policy = Politicas.OperarioDeposito)]
internal sealed class DetalleModel(ConfirmarRendicionHandler handler, Logistica.Modules.Planificacion.Contracts.IPlanificacionModuleApi rutas) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid RutaId { get; set; }
    [BindProperty] public long? RevisionEsperada { get; set; }
    [BindProperty] public Guid OperacionId { get; set; } = Guid.NewGuid();

    // Un código por línea: el lector escribe el código y presiona Enter.
    [BindProperty]
    public string? Escaneados { get; set; } = "";

    [BindProperty]
    public bool ConfirmarFaltantes { get; set; }

    [BindProperty]
    public bool ConfirmarNoCargadosEnDeposito { get; set; }

    public bool RequiereConfirmacion { get; private set; }

    public ComparacionRendicion Comparacion { get; private set; } = null!;

    // Recién después de comparar se muestran faltantes y sobrantes.
    public bool Comparada { get; private set; }

    public string? Mensaje { get; private set; }

    public int NoCargadosRecuperables { get; private set; }

    public async Task<IActionResult> OnPostRecuperarNoCargadosAsync(CancellationToken ct)
    {
        try
        {
            if (!ConfirmarNoCargadosEnDeposito)
                throw new DomainException("Confirmá que todos los bultos de los envíos no cargados están en depósito antes de liberarlos.");
            if (await handler.RecuperarNoCargadosAsync(RutaId, ct, RevisionEsperada, OperacionId) is not { } cantidad) return NotFound();
            TempData["Mensaje"] = $"Se liberaron bultos confirmados en depósito de {cantidad} envíos para una nueva planificación.";
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
        }
        return LocalRedirect($"/backoffice/rendiciones/{RutaId}");
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        if (TempData["Error"] is string error)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        Escaneados = TempData["Escaneados"] as string ?? "";
        Comparada = Escaneados.Length > 0;
        return await MostrarAsync(ct);
    }

    public Task<IActionResult> OnPostCompararAsync(CancellationToken ct)
    {
        Comparada = true;
        return MostrarAsync(ct);
    }

    public async Task<IActionResult> OnPostConfirmarAsync(CancellationToken ct)
    {
        try
        {
            var comparacion = await handler.CompararAsync(RutaId, Codigos(), ct);
            if (comparacion is null) return NotFound();
            if (comparacion.Rendicion.FinalizadaEn is null && comparacion.Sobrantes.Count == 0 &&
                comparacion.Faltantes > 0 && !ConfirmarFaltantes)
            {
                Comparacion = comparacion;
                Comparada = true;
                RequiereConfirmacion = true;
                return Page();
            }

            if (await handler.ConfirmarAsync(RutaId, Codigos(), ct, ConfirmarNoCargadosEnDeposito, RevisionEsperada, OperacionId) is not { } confirmada)
            {
                return NotFound();
            }

            TempData["Mensaje"] = $"Rendición confirmada: {confirmada.Devueltos} bultos recibidos, " +
                $"{confirmada.Reintegrados} envíos con bultos reintegrados al depósito y {confirmada.Faltantes} bultos faltantes " +
                "con incidencia. La ruta quedó finalizada.";
            return LocalRedirect("/backoffice/rendiciones");
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
            TempData["Escaneados"] = Escaneados;
            return LocalRedirect($"/backoffice/rendiciones/{RutaId}");
        }
    }

    private string[] Codigos() => (Escaneados ?? "").Split(['\n', '\r', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await handler.CompararAsync(RutaId, Codigos(), ct) is not { } comparacion)
        {
            return NotFound();
        }

        Comparacion = comparacion;
        RevisionEsperada = await rutas.RevisionAsync(RutaId, ct);
        ModelState.Remove(nameof(RevisionEsperada));
        NoCargadosRecuperables = (await handler.NoCargadosRecuperablesAsync(comparacion.Rendicion, ct)).Count;
        return Page();
    }

    public static string Situacion(string situacion) => situacion switch
    {
        "NoIntentado" => "No intentado",
        "IntentoFallido" => "Intento fallido",
        "CargaParcial" => "Carga parcial",
        "NoCargado" => "No se cargó",
        "EntregadoEnCalle" => "Entregado",
        _ => situacion,
    };
}
