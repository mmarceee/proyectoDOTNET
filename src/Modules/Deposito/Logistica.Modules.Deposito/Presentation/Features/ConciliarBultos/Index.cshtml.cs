using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.ConciliarBultos;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Deposito.Presentation.Features.ConciliarBultos;

[Authorize(Policy = Politicas.OperarioDeposito)]
internal sealed class IndexModel(IEnviosModuleApi envios, IPlanificacionModuleApi rutas,
    ConciliarBultosHandler handler) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? CodigoBulto { get; set; }
    [BindProperty] public Guid OperacionId { get; set; }
    [BindProperty] public Guid? RutaId { get; set; }
    [BindProperty] public long? RevisionRuta { get; set; }
    [BindProperty] public bool EnDeposito { get; set; }
    [BindProperty] public string Motivo { get; set; } = "";
    [BindProperty] public List<LineaSeleccionCustodia> Lineas { get; set; } = [];
    public string? Numero { get; private set; }
    public IReadOnlyList<GrupoCustodia> Grupos { get; private set; } = [];
    public string? Mensaje => TempData["CustodiaConfirmada"] as string;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(CodigoBulto)) return Page();
        var envio = await envios.ObtenerParaRecepcionAsync(CodigoBulto.Trim().ToUpperInvariant(), ct);
        if (envio is null) { ModelState.AddModelError("", "Bulto desconocido."); return Page(); }
        Numero = envio.Numero;
        var bultos = await envios.ConsultarCustodiaAsync([envio.EnvioId], ct);
        var activas = await rutas.ConsultarReservasActivasAsync(bultos.Select(b => b.BultoId).ToArray(), ct);
        var grupos = new List<GrupoCustodia>();
        foreach (var grupo in bultos.Where(b => b.Estado is not ("Entregado" or "Devuelto" or "Cancelado" or "Extraviado"))
            .GroupBy(b => activas.SingleOrDefault(r => r.BultoId == b.BultoId)?.RutaId))
            grupos.Add(new(grupo.Key, grupo.Key is Guid ruta ? await rutas.RevisionAsync(ruta, ct) : null,
                grupo.OrderBy(b => b.Codigo).ToArray()));
        Grupos = grupos;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Forbid();
        try
        {
            if (!ModelState.IsValid) throw new DomainException("Revisá los datos de conciliación.");
            var pedido = new ConciliacionBultos(OperacionId, RutaId, RevisionRuta,
                Lineas.Where(l => l.Seleccionado).Select(l => new ConfirmacionCustodiaBulto(l.BultoId, l.Revision)).ToArray(),
                EnDeposito, Motivo, actor);
            var cantidad = await handler.HandleAsync(pedido, ct);
            TempData["CustodiaConfirmada"] = EnDeposito ? $"Se confirmó la presencia de {cantidad} bultos en depósito."
                : $"Se registraron {cantidad} bultos no localizados. Continúan bloqueados para planificación.";
        }
        catch (DomainException e) { TempData["ErrorCustodia"] = e.Message; }
        return RedirectToPage(new { CodigoBulto });
    }
}

internal sealed record GrupoCustodia(Guid? RutaId, long? Revision, IReadOnlyList<CustodiaBulto> Bultos);
internal sealed class LineaSeleccionCustodia
{
    public bool Seleccionado { get; set; }
    public Guid BultoId { get; set; }
    public long Revision { get; set; }
}
