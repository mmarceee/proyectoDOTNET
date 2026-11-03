using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Deposito.Application.Features.GenerarLiquidacion;
using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Deposito.Presentation.Features.GenerarLiquidacion;

// CU-32: el administrador elige el comercio y el período, y genera la liquidación en borrador. Si no se genera (A1, o
// un período inválido), vuelve con un GET nuevo y el motivo en TempData, como CU-31 y CU-55.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(GenerarLiquidacionHandler handler, TimeProvider reloj) : PageModel
{
    [BindProperty]
    public Guid ComercioId { get; set; }

    [BindProperty]
    public DateOnly Desde { get; set; }

    [BindProperty]
    public DateOnly Hasta { get; set; }

    public IReadOnlyList<ComercioRelacionado> Comercios { get; private set; } = [];
    public IReadOnlyList<Liquidacion> Liquidaciones { get; private set; } = [];
    public string? Mensaje { get; private set; }

    public string NombreComercio(Guid id) => Comercios.FirstOrDefault(c => c.ComercioId == id)?.RazonSocial ?? "—";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        if (TempData["Error"] is string error) ModelState.AddModelError(string.Empty, error);

        // Por defecto, el mes anterior completo: el período cerrado más reciente.
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
        Desde = DateOnly.TryParse(TempData["Desde"]?.ToString(), out var desde) ? desde : inicioMes.AddMonths(-1);
        Hasta = DateOnly.TryParse(TempData["Hasta"]?.ToString(), out var hasta) ? hasta : inicioMes.AddDays(-1);
        ComercioId = Guid.TryParse(TempData["ComercioId"]?.ToString(), out var comercio) ? comercio : Guid.Empty;

        Comercios = await handler.ComerciosAsync(ct);
        Liquidaciones = await handler.ListarAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var liquidacion = await handler.GenerarAsync(new GenerarLiquidacionCommand(ComercioId, Desde, Hasta), ct);
            var envios = liquidacion.Lineas.Count == 1 ? "1 envío" : $"{liquidacion.Lineas.Count} envíos";
            TempData["Mensaje"] = $"Se generó la liquidación con {envios}. Revisala y emitila.";
            return LocalRedirect($"/backoffice/liquidaciones/{liquidacion.Id}");
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
            TempData["ComercioId"] = ComercioId.ToString();
            TempData["Desde"] = Desde.ToString("yyyy-MM-dd");
            TempData["Hasta"] = Hasta.ToString("yyyy-MM-dd");
            return LocalRedirect("/backoffice/liquidaciones");
        }
    }
}
