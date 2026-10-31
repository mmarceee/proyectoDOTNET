using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.ConfirmarDevolucion;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Deposito.Presentation.Features.ConfirmarDevolucion;

// CU-31: el operario busca el envío devuelto, marca los bultos que entrega y registra quién los recibe por el comercio.
// Una devolución de otro operador no se encuentra (ADR-0002, sección 2.9). Si la confirmación no se completa, se vuelve a
// la página con un GET nuevo, como en CU-55: la transacción terminó y no se consulta de nuevo en el mismo request. Lo que
// cargó el operario viaja en TempData.
[Authorize(Policy = Politicas.OperarioDeposito)]
internal sealed class IndexModel(ConfirmarDevolucionHandler handler) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Buscar { get; set; }

    [BindProperty]
    public List<Guid> Entregados { get; set; } = [];

    [BindProperty]
    public string? NombreReceptor { get; set; }

    [BindProperty]
    public string? DocumentoReceptor { get; set; }

    [BindProperty]
    public bool ConfirmarParcial { get; set; }

    public DevolucionParaEntrega? Devolucion { get; private set; }
    public bool FaltanBultos { get; private set; }
    public string? Mensaje { get; private set; }
    public bool Buscado => !string.IsNullOrWhiteSpace(Buscar);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        if (TempData["Error"] is string error) ModelState.AddModelError(string.Empty, error);
        FaltanBultos = TempData["FaltanBultos"] as string == "si";
        // ToString: TempData devuelve como Guid o fecha un texto que tiene esa forma (por ejemplo, un solo bulto marcado).
        NombreReceptor = TempData["NombreReceptor"]?.ToString();
        DocumentoReceptor = TempData["DocumentoReceptor"]?.ToString();
        if (!Buscado) return;

        Devolucion = await handler.ConsultarAsync(Buscar!, ct);
        // Por defecto se entregan todos los bultos; el operario desmarca los que faltan (A2).
        Entregados = TempData["Entregados"]?.ToString() is { } marcados
            ? marcados.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList()
            : Devolucion?.Bultos.Select(b => b.Id).ToList() ?? [];
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario);
        try
        {
            var resultado = await handler.ConfirmarAsync(new ConfirmarDevolucionCommand(Buscar ?? "", Entregados,
                NombreReceptor, DocumentoReceptor, ConfirmarParcial, usuario == Guid.Empty ? null : usuario), ct);
            switch (resultado.Estado)
            {
                case EstadoConfirmacion.NoEncontrada:
                    return NotFound();
                case EstadoConfirmacion.Confirmada:
                    TempData["Mensaje"] = $"Se entregó la devolución del envío {resultado.Numero} al comercio.";
                    return LocalRedirect("/backoffice/devoluciones");
                default:
                    TempData["FaltanBultos"] = "si";
                    break;
            }
        }
        catch (DomainException e)
        {
            TempData["Error"] = e.Message;
        }

        TempData["Entregados"] = string.Join(",", Entregados);
        TempData["NombreReceptor"] = NombreReceptor;
        TempData["DocumentoReceptor"] = DocumentoReceptor;
        return LocalRedirect($"/backoffice/devoluciones?Buscar={Uri.EscapeDataString(Buscar ?? "")}");
    }
}
